#!/usr/bin/env python3
"""
Universal NSIS 3.x Installer Extractor
=======================================
Reverse-engineered from Unity NSIS installers using IDA Pro.

Supports:
- Variable firstheader sizes (28, 32, 36, 40 bytes)
- Header metadata prefixes (8-byte decompressed size prepended)
- Per-file individual compression (e.g. 62f1)
- Multi-block solid compression (e.g. 73f1)
- Directory structure preservation
"""
import bisect
import shutil
import struct
import lzma
import os
import re
import sys
import glob
from collections import defaultdict


def find_installers():
    """Return list of (installer_path, output_dir) pairs to process."""
    if len(sys.argv) > 1:
        # All positional args are installer paths; output = name without .exe
        paths = sys.argv[1:]
        return [(p, os.path.splitext(os.path.basename(p))[0]) for p in paths]
    # No args: scan cwd for NSIS exe files
    results = []
    for c in sorted(glob.glob('*.exe')):
        with open(c, 'rb') as f:
            if b'\xef\xbe\xad\xdeNullsoftInst' in f.read(1024 * 1024):
                results.append((c, os.path.splitext(c)[0]))
    if not results:
        print("Usage: python3 nsis_extract.py [installer.exe ...]")
        print("       (or run from a directory containing Unity installer .exe files)")
        sys.exit(1)
    return results


def decode_nsis_string(data, strings_offset, ptr):
    """Decode NSIS Unicode string. ptr is a WORD (2-byte) index."""
    start = strings_offset + ptr * 2
    if start >= len(data):
        return ""
    res = []
    i = start
    while i < len(data) - 1:
        ch = struct.unpack_from('<H', data, i)[0]
        if ch == 0:
            break
        if 1 <= ch <= 4:
            # ANSI-mode NSIS escape: code 2=var, 3=shell folder
            code = ch
            i += 2
            if i + 1 >= len(data):
                break
            val = struct.unpack_from('<H', data, i)[0]
            i += 2
            if code == 2:
                names = {20: '$CMDLINE', 21: '$INSTDIR', 22: '$OUTDIR',
                         23: '$EXEDIR', 25: '$TEMP', 26: '$PLUGINSDIR'}
                res.append(names.get(val, f'$V({val:x})'))
            elif code == 3:
                names = {0: '$DESKTOP', 2: '$SMPROGRAMS', 5: '$DOCUMENTS',
                         7: '$STARTMENU', 26: '$APPDATA', 35: '$LOCALAPPDATA'}
                res.append(names.get(val, f'$SHELL({val:x})'))
        elif 0xE001 <= ch <= 0xE004:
            # Unicode-mode NSIS escape: 0xE000+code, next char = 0x8000+val
            code = ch - 0xE000  # 1=var, 2=skip, 3=shell, 4=lang
            i += 2
            if i + 1 >= len(data):
                break
            val = struct.unpack_from('<H', data, i)[0] - 0x8000
            i += 2
            if code == 1:
                names = {20: '$CMDLINE', 21: '$INSTDIR', 22: '$OUTDIR',
                         23: '$EXEDIR', 25: '$TEMP', 26: '$PLUGINSDIR'}
                res.append(names.get(val, f'$V({val:x})'))
            elif code == 3:
                names = {0: '$DESKTOP', 2: '$SMPROGRAMS', 5: '$DOCUMENTS',
                         7: '$STARTMENU', 26: '$APPDATA', 35: '$LOCALAPPDATA'}
                res.append(names.get(val, f'$SHELL({val:x})'))
        else:
            res.append(chr(ch))
            i += 2
    return "".join(res)


def sanitize_path(p):
    """Convert NSIS variable-laden path to safe local path."""
    for var in ['$INSTDIR', '$OUTDIR', '$EXEDIR', '$TEMP', '$PLUGINSDIR',
                '$DESKTOP', '$SMPROGRAMS', '$DOCUMENTS', '$STARTMENU',
                '$APPDATA', '$LOCALAPPDATA', '$CMDLINE']:
        p = p.replace(var + '\\', '').replace(var, '')
    p = re.sub(r'\$[A-Z_]+\([^)]*\)', '', p)
    p = re.sub(r'\$V\([^)]*\)', '', p)
    p = re.sub(r'\$\([^)]*\)', '', p)
    p = p.replace('\\', os.sep)
    p = p.strip(os.sep + ' ')
    return p if p else '_unnamed'


def decompress_nsis_lzma(data, pos, expected_size=-1):
    """Decompress NSIS LZMA block: [3-byte nc][nc bytes: 5-byte props + compressed].

    Tries the bit23-masked count first (standard NSIS, bit23 = solid-mode flag),
    then the full 24-bit count as a fallback for installers that use all 24 bits
    (e.g. Unity 2021 header).  Uses LZMADecompressor.unused_data to report the
    *actual* bytes consumed so that callers compute data_base correctly even when
    nc_full overshoots the real LZMA stream end.
    """
    if pos + 8 > len(data):
        return None, 0
    nc_masked = data[pos] | (data[pos + 1] << 8) | ((data[pos + 2] & 0x7F) << 16)
    nc_full   = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16)
    candidates = [nc_masked] if nc_masked == nc_full else [nc_masked, nc_full]

    for nsis_cnt in candidates:
        if nsis_cnt == 0 or pos + 3 + nsis_cnt > len(data):
            continue
        lzma_data = data[pos + 3 : pos + 3 + nsis_cnt]
        if len(lzma_data) < 5 or lzma_data[0] != 0x5D:
            continue
        size = expected_size if expected_size > 0 else -1
        stream = lzma_data[0:5] + struct.pack('<q', size) + lzma_data[5:]
        try:
            d = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE)
            dec = d.decompress(stream)
            if not d.eof:
                continue  # truncated stream (nc too small); try next candidate
            actual = (len(stream) - len(d.unused_data)) - 8  # subtract fake size field
            return dec, 3 + actual
        except lzma.LZMAError:
            if size != -1:
                try:
                    s2 = lzma_data[0:5] + struct.pack('<q', -1) + lzma_data[5:]
                    d = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE)
                    dec = d.decompress(s2)
                    if not d.eof:
                        continue
                    actual = (len(s2) - len(d.unused_data)) - 8
                    return dec, 3 + actual
                except lzma.LZMAError:
                    pass
    return None, 0


class NSISDataScanner:
    """Manages virtual concatenated stream for Solid Compression."""

    def __init__(self, data, data_base):
        self.data = data
        self.data_base = data_base
        self.blocks = []  # (phys_pos, comp_len, v_start, v_end)
        self._v_starts = []  # parallel list of v_start for bisect lookups
        self.cache = {}
        self.max_cache = 32
        self._scan()

    def _scan(self):
        # Decompress the first block to discover the standard decompressed block size,
        # then skip decompression for all subsequent blocks (just read the 3-byte nc to
        # compute consumed bytes). Decompress the last block too, since it may be smaller.
        print("  Scanning blocks...")
        pos = self.data_base
        v_offset = 0
        block_size = None  # decompressed size of a full block

        while pos + 3 < len(self.data):
            if self.data[pos + 3] != 0x5D:
                break
            nc = self.data[pos] | (self.data[pos + 1] << 8) | ((self.data[pos + 2] & 0x7F) << 16)
            if nc == 0 or pos + 3 + nc > len(self.data):
                break

            if block_size is None:
                # First block: decompress to find block_size and verify nc
                dec, consumed = decompress_nsis_lzma(self.data, pos)
                if dec is None:
                    break
                block_size = len(dec)
                d_len = block_size
            else:
                consumed = 3 + nc
                d_len = block_size  # assumed; corrected for last block below

            self.blocks.append((pos, consumed, v_offset, v_offset + d_len))
            self._v_starts.append(v_offset)
            v_offset += d_len
            pos += consumed
            if len(self.blocks) % 500 == 0:
                print(f"    ... found {len(self.blocks)} blocks ({v_offset // 1024 // 1024}MB virtual)")

        # Correct the last block's decompressed size (it may be a partial block)
        if len(self.blocks) > 1:
            last = self.blocks[-1]
            dec_last, _ = decompress_nsis_lzma(self.data, last[0])
            if dec_last is not None and len(dec_last) != block_size:
                self.blocks[-1] = (last[0], last[1], last[2], last[2] + len(dec_last))

        print(f"  Total: {len(self.blocks)} blocks, {v_offset // 1024 // 1024}MB virtual")

    def _find_block(self, v_start):
        """Binary search: return index of block containing v_start, or -1."""
        idx = bisect.bisect_right(self._v_starts, v_start) - 1
        if 0 <= idx < len(self.blocks) and self.blocks[idx][3] > v_start:
            return idx
        return -1

    def _load_block(self, target):
        if target not in self.cache:
            if len(self.cache) >= self.max_cache:
                self.cache.pop(next(iter(self.cache)))
            dec, _ = decompress_nsis_lzma(self.data, self.blocks[target][0])
            if dec is None:
                return None
            self.cache[target] = dec
        return self.cache[target]

    def get_data(self, v_start, length):
        res = bytearray()
        needed = length
        curr_v = v_start
        while needed > 0:
            target = self._find_block(curr_v)
            if target == -1:
                break
            b_dec = self._load_block(target)
            if b_dec is None:
                break
            off = curr_v - self.blocks[target][2]
            to_read = min(needed, len(b_dec) - off)
            if to_read <= 0:
                break
            res.extend(b_dec[off:off + to_read])
            curr_v += to_read
            needed -= to_read
        return bytes(res)

    def write_data(self, v_start, length, fileobj):
        """Write length bytes from virtual offset v_start directly to fileobj (no buffering)."""
        needed = length
        curr_v = v_start
        while needed > 0:
            target = self._find_block(curr_v)
            if target == -1:
                break
            b_dec = self._load_block(target)
            if b_dec is None:
                break
            off = curr_v - self.blocks[target][2]
            to_write = min(needed, len(b_dec) - off)
            if to_write <= 0:
                break
            fileobj.write(b_dec[off:off + to_write])
            curr_v += to_write
            needed -= to_write


def decompress_block_individual(data, pos):
    """Decompress one per-file block at physical pos (62f1 format: 8-byte header + NSIS LZMA)."""
    if pos + 11 > len(data): return None
    hdr = struct.unpack_from('<q', data, pos)[0]
    is_lzma = hdr < 0
    size = hdr & 0x7FFFFFFFFFFFFFFF
    if not is_lzma:
        return bytes(data[pos+8 : pos+8+size])
    dec, _ = decompress_nsis_lzma(data, pos + 8)
    return dec


def decompress_block_2021(data, pos):
    """Decompress one per-file block (2021 format: [3-byte nc][1-byte flag][nc bytes]).
    flag=0x00: raw uncompressed data. flag&0x80: LZMA (props byte first)."""
    if pos + 4 > len(data):
        return None
    nc = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16)
    flag = data[pos + 3]
    if pos + 4 + nc > len(data):
        return None
    payload = data[pos + 4 : pos + 4 + nc]
    if not (flag & 0x80):
        return bytes(payload)  # raw/uncompressed or pre-compressed (nc=0 → empty file)
    if len(payload) < 5:
        return None
    props = payload[0]
    pb = props // 45; rem = props % 45; lp = rem // 9; lc = rem % 9
    if pb > 4 or lp > 4 or lc > 8 or lc + lp > 4:
        return None
    stream = payload[:5] + struct.pack('<q', -1) + payload[5:]
    try:
        d = lzma.LZMADecompressor(format=lzma.FORMAT_ALONE)
        return d.decompress(stream)  # accept eof=False: some encoders omit EOS marker
    except lzma.LZMAError:
        return None


def find_header_block(data, fh_off, loh):
    for scan_off in range(fh_off + 28, min(fh_off + 52, len(data) - 5)):
        if data[scan_off] != 0x5D: continue
        nsis_off = scan_off - 3
        # Try 8-byte header (62f1 style): validate that the header's declared
        # decompressed size matches what LZMA actually produces, then use the
        # real consumed byte count (not the declared size) for data_base.
        if nsis_off - 8 >= fh_off + 24:
            hdr_pos = nsis_count_off = nsis_off - 8
            hdr = struct.unpack_from('<q', data, hdr_pos)[0]
            if hdr < 0:
                dec, consumed = decompress_nsis_lzma(data, hdr_pos + 8)
                if dec and len(dec) == (hdr & 0x7FFFFFFFFFFFFFFF):
                    return hdr_pos, 8, 8 + consumed, dec
        # Try direct NSIS (73f1 / extended-firsthdr style): Unity's hdrsize
        # field does not equal the decompressed size of the header block, so
        # accept any successful decompression that produces a plausible result.
        dec, consumed = decompress_nsis_lzma(data, nsis_off)
        if dec and len(dec) > 1024:
            # Some installers (e.g. Unity 6000.4.x) split the header across
            # multiple consecutive LZMA blocks. Keep reading until we reach loh.
            total_consumed = consumed
            chunks = [dec]
            pos = nsis_off + consumed
            while sum(len(c) for c in chunks) < loh:
                more, c = decompress_nsis_lzma(data, pos)
                if not more or c == 0:
                    break
                chunks.append(more)
                total_consumed += c
                pos += c
            full_dec = b''.join(chunks) if len(chunks) > 1 else dec
            return nsis_off, 0, total_consumed, full_dec
    return None, 0, 0, None


def extract_one(installer, output_dir):
    print(f"\nReading {installer}...")
    with open(installer, 'rb') as f:
        data = f.read()

    idx = data.find(b'\xef\xbe\xad\xdeNullsoftInst')
    if idx < 0:
        print(f"  Not an NSIS installer, skipping.")
        return
    fh_off = idx - 4
    loh = struct.unpack_from('<I', data, fh_off + 20)[0]

    h_pos, b_h_sz, b_tot, h_data = find_header_block(data, fh_off, loh)
    if not h_data:
        print(f"  Could not parse header, skipping.")
        return

    data_base = h_pos + b_tot

    h_base = 8 if struct.unpack_from('<I', h_data, 0)[0] == loh else 0
    e_off, e_cnt = struct.unpack_from('<II', h_data, h_base + 20)
    s_off = struct.unpack_from('<I', h_data, h_base + 28)[0]
    e_off += h_base; s_off += h_base

    # NSIS 2.x/early-3.x: entry struct is 7 ints (28 bytes).
    # NSIS 3.x modern (Unity 2021+): entry struct is 9 ints (36 bytes).
    space = s_off - e_off
    stride = 28 if space == e_cnt * 28 else 36
    fmt = f'<{stride // 4}I'

    files = []
    cdir = ''
    for i in range(e_cnt):
        if e_off + i * stride + stride > len(h_data):
            break
        v = struct.unpack_from(fmt, h_data, e_off + i * stride)
        if v[0] == 11: cdir = sanitize_path(decode_nsis_string(h_data, s_off, v[1]))
        elif v[0] == 20:
            name = sanitize_path(decode_nsis_string(h_data, s_off, v[2]))
            if name:
                off = ((v[4] << 32) | v[3]) if stride == 36 else v[3]
                files.append((os.path.join(cdir, name) if cdir else name, off))

    # Detect data format by scanning from data_base:
    #
    # Unity 62f1 (per-file):  2 auxiliary NSIS LZMA solid blocks → [00 00 00] separator
    #                          → per-file blocks with 8-byte header + NSIS LZMA
    # Unity 2021 (per-file):  no auxiliary blocks; per-file blocks start directly at
    #                          data_base using [3-byte nc][1-byte flag][nc bytes LZMA]
    # Unity 73f1 (solid):     continuous NSIS LZMA blocks, no separator pattern
    is_solid = True
    per_file_base = data_base
    format_2021 = False
    pos = data_base
    for _ in range(50):
        _, consumed = decompress_nsis_lzma(data, pos)
        if consumed == 0:
            break
        pos += consumed
    if pos + 11 <= len(data) and data[pos:pos+3] == b'\x00\x00\x00':
        hdr_test = struct.unpack_from('<q', data, pos + 3)[0]
        if hdr_test < 0:
            is_solid = False
            per_file_base = pos + 3
    if is_solid:
        # Guard: if data_base itself is a valid NSIS LZMA block, it's a solid stream.
        # decompress_block_2021 always returns non-None for raw-flagged blocks (flag
        # bit7=0), and solid blocks have props=0x5D (bit7=0) — so 2021 detection fires
        # as a false positive unless we test for NSIS LZMA first.
        dec_nsis_check, _ = decompress_nsis_lzma(data, data_base)
        if dec_nsis_check is None:
            d_test = decompress_block_2021(data, data_base)
            if d_test is not None:
                is_solid = False
                format_2021 = True
                per_file_base = data_base

    fmt_name = 'Solid' if is_solid else ('2021-per-file' if format_2021 else 'Per-file')
    print(f"  Format: {fmt_name}. Extracting {len(files)} files → {output_dir}/")

    scanner = NSISDataScanner(data, data_base) if is_solid else None
    extracted, errors = 0, 0
    os.makedirs(output_dir, exist_ok=True)

    if is_solid:
        # Group paths by virtual offset so each unique blob is decompressed once.
        # Duplicates (same offset, different paths) get hardlinked from the first copy.
        off_to_paths = defaultdict(list)
        seen_pairs = set()
        for path, off in files:
            if (path, off) not in seen_pairs:
                off_to_paths[off].append(path)
                seen_pairs.add((path, off))

        for off, paths in sorted(off_to_paths.items()):
            first_target = os.path.join(output_dir, paths[0])
            try:
                hdr = scanner.get_data(off, 8)
                sz = struct.unpack('<I', hdr[:4])[0]
                os.makedirs(os.path.dirname(first_target) or '.', exist_ok=True)
                with open(first_target, 'wb') as f:
                    scanner.write_data(off + 8, sz, f)
                extracted += 1
                for alt_path in paths[1:]:
                    alt_target = os.path.join(output_dir, alt_path)
                    os.makedirs(os.path.dirname(alt_target) or '.', exist_ok=True)
                    try:
                        os.link(first_target, alt_target)
                    except OSError:
                        shutil.copy2(first_target, alt_target)
                    extracted += 1
            except Exception:
                errors += len(paths)
    else:
        for path, off in files:
            target = os.path.join(output_dir, path)
            try:
                if format_2021:
                    fdata = decompress_block_2021(data, per_file_base + off)
                else:
                    fdata = decompress_block_individual(data, per_file_base + off)
                if fdata is not None:
                    os.makedirs(os.path.dirname(target) or '.', exist_ok=True)
                    with open(target, 'wb') as f: f.write(fdata)
                    extracted += 1
                else:
                    errors += 1
            except Exception:
                errors += 1

    print(f"  Done! {extracted} files saved, {errors} errors.")

def main():
    extract_one(sys.argv[1], sys.argv[2])

if __name__ == "__main__":
    main()

using System.IO.Compression;

namespace Generator;

internal static class ZipHandler
{
    internal static void Append(
        string outputFilePath, 
        string targetPath, 
        string searchPattern)
    {
        bool fileExists = File.Exists(outputFilePath);
        using var managedZipStr = File.Open(outputFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        using (var managedZip = new ZipArchive(managedZipStr, fileExists 
                   ? ZipArchiveMode.Update 
                   : ZipArchiveMode.Create, true))
        {
            foreach (var file in Directory.EnumerateFiles(targetPath, searchPattern))
                managedZip.CreateEntryFromFile(file, Path.GetFileName(file));
        }
        managedZipStr.Close();
    }
}
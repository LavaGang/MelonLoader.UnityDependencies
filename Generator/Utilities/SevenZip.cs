using System.Diagnostics;

namespace Generator;

internal static class SevenZip
{
    private static readonly string sevenZipPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Dependencies", "7zz");

    internal static async Task ExtractAsync(string archive, 
        string outputDir,
        bool keepStructure,
        params string[] fileFilter)
    {
        var args = new List<string>()
        {
            keepStructure ? "x" : "e", archive, "-y", $"-o{outputDir}"
        };
        
        args.AddRange(fileFilter);
        
        var proc = Process.Start(new ProcessStartInfo(sevenZipPath, args)
        {
            //UseShellExecute = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        })!;
        proc.OutputDataReceived += (_, eventArgs) => Console.WriteLine(eventArgs.Data); 
        proc.ErrorDataReceived += (_, eventArgs) => Console.WriteLine(eventArgs.Data); 
        await proc.WaitForExitAsync();
        
        //if (proc.ExitCode != 0)
        //    throw new($"7z exit code: {proc.ExitCode}");
    }
}
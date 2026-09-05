using System.Diagnostics;

namespace Generator;

internal static class NSISExtractor
{
    private static readonly string scriptPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Dependencies", "nsis_extract.py");
    
    internal static async Task ExtractAsync(string archive, string outputDir)
    {
        var args = new List<string>()
        {
            scriptPath,
            archive,
            outputDir,
        };
        
        var proc = Process.Start(new ProcessStartInfo("python", args)
        {
            //UseShellExecute = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        })!;
        proc.OutputDataReceived += (_, eventArgs) => Console.WriteLine(eventArgs.Data); 
        proc.ErrorDataReceived += (_, eventArgs) => Console.WriteLine(eventArgs.Data); 
        await proc.WaitForExitAsync();
        
        if (proc.ExitCode != 0)
            throw new($"7z exit code: {proc.ExitCode}");
    }
}
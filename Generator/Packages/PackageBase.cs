using System.Runtime.InteropServices;
using Octokit;

namespace Generator.Packages;

internal class PackageBase
{
    private const string _searchMscorlib = "mscorlib.dll";
    private const string _searchUnityEngine = "UnityEngine.dll";
    
    internal UnityPlatformID Platform { get; init; }
    internal Architecture Arch { get; init; }
    internal ePackageType Type { get; init; }

    internal string? DownloadPath { get; set; }
    internal string? ExtractedPath { get; set; }
    internal string? TargetPath { get; private set; }

    internal string? PackageName { get; private set; }
    internal string? PackagePath { get; set; }

    private List<string>? TargetFilters { get; set; }

    internal virtual async Task<bool> Download(UnityVersion unityVersion)
    {
        DownloadPath = await PackageHandler.Download(unityVersion, Platform, Arch, Type);
        return !string.IsNullOrEmpty(DownloadPath);
    }
    
    internal virtual async Task<bool> Extract()
    {
        // Validate Path
        if (string.IsNullOrEmpty(DownloadPath))
            return false;
        
        // Extract the Package
        ExtractedPath = Path.Combine(Program._tempDir, Enum.GetName(Type)!);
        PackageHandler.RecreateDirectory(ExtractedPath);
        await PackageHandler.Extract(Platform, DownloadPath, ExtractedPath);
        
        // Validate Path
        return !string.IsNullOrEmpty(ExtractedPath);
    }

    internal virtual async Task Bundle(Release? release) => await Task.CompletedTask;

    internal virtual async Task Upload(Release? release)
    {
        if (!Config.GitHubUploadPackages
            || (release == null))
            return;
        
        Console.WriteLine($"Uploading {PackageName}");
        await GitHubAPI.UploadFile(PackagePath!, release);
    }

    internal bool FindTargetFolder()
    {
        // Validate Path
        if (string.IsNullOrEmpty(ExtractedPath))
            return false;
        
        // Get Filters
        TargetFilters ??= PackageHandler.GetFilters(Platform, Arch, Type);
        PackageName = PackageHandler.GetPackageName(Platform, Arch);
        PackagePath = Path.Combine(Program._tempDir, PackageName);
        TargetPath = PackageHandler.FindFilteredFolder(ExtractedPath, 
            (Type == ePackageType.Setup) 
                ? _searchMscorlib 
                : _searchUnityEngine, 
            TargetFilters);
        
        // Validate Path
        return !string.IsNullOrEmpty(TargetPath);
    }
    
    internal bool BundlePackage(PackageBase package)
    {
        string? originalExtractedPath = package.ExtractedPath;
        if (!package.FindTargetFolder())
            package.ExtractedPath = ExtractedPath;
        if (!package.FindTargetFolder())
        {
            Console.WriteLine($"Unable to find Target Folder in {originalExtractedPath} or {ExtractedPath}");
            return false;
        }

        PackageHandler.Bundle(this, package);
        return true;
    }
}
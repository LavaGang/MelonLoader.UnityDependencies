using System.Runtime.InteropServices;
using Octokit;

namespace Generator.Packages;

internal class PackageDownload
    : PackageBase
{
    private readonly PackageBase _component;
    private readonly PackageBase? _componentX86;

    internal PackageDownload(UnityPlatformID platform, Architecture arch)
    {
        // Create Setup Package
        Platform = platform;
        Arch = arch;
        Type = ePackageType.Setup;

        // Create Component Package
        _component = new()
        {
            Platform = platform,
            Arch = arch,
            Type = ePackageType.Component
        };
        
        // Create Component Package for x86
        if (arch == Architecture.X64)
            _componentX86 = new()
            {
                Platform = platform,
                Arch = Architecture.X86,
                Type = ePackageType.Component
            };
    }

    internal override async Task<bool> Download(UnityVersion unityVersion)
    {
        // Download Setup
        if (!await base.Download(unityVersion))
            return false;

        // Download Component
        await _component.Download(unityVersion);
        
        // Return Success
        _componentX86?.DownloadPath = _component.DownloadPath;
        return true;
    }

    internal override async Task<bool> Extract()
    {
        // Extract Setup
        if (!await base.Extract())
            return false;
        
        // Extract Component
        await _component.Extract();
        
        // Return Success
        _componentX86?.ExtractedPath = _component.ExtractedPath;
        return true;
    }

    internal override async Task Bundle(Release? release)
    {
        // Find Setup Folder
        if (!FindTargetFolder())
        {
            Console.WriteLine($"Unable to find Target Folder in {ExtractedPath}");
            return;
        }
        
        // Bundle
        if (BundlePackage(_component))
            await _component.Upload(release);
        
        // Bundle x86
        if ((_componentX86 != null)
            && BundlePackage(_componentX86))
            await _componentX86.Upload(release);
    }
}
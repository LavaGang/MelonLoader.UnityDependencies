using System.Runtime.InteropServices;
using Generator.Packages;
using Octokit;
using Semver;

namespace Generator;

internal static class Program
{
    internal static readonly string _tempDir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Temp");

    private const string _releaseBody =
        "Automatically generated and uploaded by the MelonLoader.UnityDependencies Generator";

    private static readonly UnityVersion _minVersion = new() 
    { 
        Id = string.Empty, 
        SemVer = new SemVersion(5, 3, 0, [ new("b"), new(1) ]),
        Major = 5, 
        Minor = 3,
        Patch = 0, 
        BuildType = 'b', 
        BuildNumber = 1
    };
    
    private static readonly PackageBase[] _packages =
    [
        // Windows
        new PackageDownload(UnityPlatformID.Windows, Architecture.X64),
        new PackageDownload(UnityPlatformID.Windows, Architecture.Arm64),
        
        // Linux
        new PackageDownload(UnityPlatformID.Linux, Architecture.X64),
        
        // MacOS
        new PackageDownload(UnityPlatformID.Mac, Architecture.X64),
        new PackageDownload(UnityPlatformID.Mac, Architecture.Arm64),
    ];

    private static IEnumerable<UnityVersion> _unityReleases = [];
    private static List<RepositoryTag> _githubTags = [];
    private static List<Release> _githubReleases = [];

    private static async Task RefreshGitHub(bool print = false)
    {
        // Fetch GitHub Tags
        if (print)
            Console.WriteLine("Fetching GitHub Tags...");
        var tagReadOnly = await GitHubAPI.GetAllTagsAsync();
        _githubTags = tagReadOnly.ToList();
        
        // Fetch GitHub Releases
        if (print)
            Console.WriteLine("Fetching GitHub Releases...");
        var releaseReadOnly = await GitHubAPI.GetAllReleasesAsync();
        _githubReleases = releaseReadOnly.ToList();
    }

    // Herp:
    // Releases set as Draft signify generation failure/cancellation
    // Releases set as Prelease signify regeneration was requested and is awaiting to be reprocessed
    private static async Task Main()
    {
        // Load Configuration
        Console.WriteLine("Loading Environment Configuration...");
        Config.Load();

        // Fetch Unity Releases
        Console.WriteLine("Fetching Unity Releases...");
        _unityReleases = await UnityAPI.GetAvailableVersionsAsync(false, false);
        if (_unityReleases.Count() <= 0)
        {
            Console.WriteLine("Failed to Fetch Unity Releases!");
            return;
        }
        
        // Set All as Prereleases to signify Regeneration
        UnityVersion? githubLatest = null;
        if (Config.GitHubUploadPackages)
        {
            // Refresh GitHub Listing
            await RefreshGitHub(true);
            githubLatest = FindLatest();
            
            if (Config.GitHubUpdateExistingReleases)
            {
                if (Config.GitHubPurgeExistingReleases)
                    Console.WriteLine("Purging Existing Releases...");
                else
                    Console.WriteLine("Applying Prerelease Tag to signify regeneration...");
                foreach (var unityVersion in _unityReleases)
                {
                    string tag = unityVersion.ToString();
                    Release? ghRel = FindGitHubRelease(tag);
                    if (ghRel == null)
                        continue;

                    if (Config.GitHubPurgeExistingReleases)
                    {
                        Console.WriteLine(ghRel.TagName);
                        await GitHubAPI.DeleteRelease(ghRel);
                        continue;
                    }
                    
                    if (ghRel is { Draft: false } and { Prerelease: false })
                    {
                        Console.WriteLine(ghRel.TagName);
                        await GitHubAPI.SetReleaseType(ghRel!, eReleaseType.Prelease);
                    }
                }
                
                Console.WriteLine("Pruning Tags without Releases...");
                foreach (var tag in _githubTags)
                {
                    string tagName = tag.Name;
                    if (FindGitHubRelease(tagName) == null)
                    {
                        Console.WriteLine(tagName);
                        await GitHubAPI.DeleteTagAsync(tagName);
                    }
                }
            }
        }

        // Process Releases
        Console.WriteLine();
        foreach (var unityVersion in _unityReleases)
        {
            // Exclude versions that aren't supported by extraction
            if (unityVersion.SemVer.ComparePrecedenceTo(_minVersion.SemVer) <= 0)
                continue;
            
            // Exclude versions that aren't specifically targeted
            string tag = unityVersion.ToString();
            if (!string.IsNullOrEmpty(Config.UnityTargetVersion)
                && (tag != Config.UnityTargetVersion)) 
                continue;

            // GitHub Handling
            RepositoryTag? githubTag = null;
            Release? githubRelease = null;
            if (Config.GitHubUploadPackages)
            {
                // Find Tag
                githubTag = FindGitHubTag(tag);
                if (githubTag == null)
                {
                    await GitHubAPI.CreateGitTag(tag, _releaseBody);
                    await RefreshGitHub();
                    githubTag = FindGitHubTag(tag);
                }

                // Find Release
                githubRelease = FindGitHubRelease(tag);
                if (githubRelease == null)
                {
                    githubRelease = await GitHubAPI.CreateRelease(tag, tag, _releaseBody, true);
                    await RefreshGitHub();
                }
                else
                {
                    // Exclude anything that is not a Draft or Prelease
                    if (githubRelease is { Draft: false } and { Prerelease: false })
                        continue;
                    
                    // Clear Release of Assets
                    Console.WriteLine($"Pruning Existing Assets...");
                    foreach (var asset in await GitHubAPI.GetAllReleaseAssets(githubRelease))
                        await GitHubAPI.DeleteAsset(asset);
                }
            }
            

            // Handle Packages
            Console.WriteLine($"Processing {tag}...");
            Console.WriteLine();
            bool success = true;
            foreach (var package in _packages)
            {
                if (await TryProcess(package, githubRelease, unityVersion))
                    continue;
                success = false;
                break;
            }
            if (!success)
                continue;

            // Set Release as Public
            if (Config.GitHubUploadPackages)
            {
                if ((githubLatest == null)
                    || (githubLatest.Value.SemVer.ComparePrecedenceTo(githubLatest.Value.SemVer) <= 0))
                {
                    githubLatest = unityVersion;
                    await GitHubAPI.SetReleaseType(githubRelease!, eReleaseType.Latest);
                }
                else 
                    await GitHubAPI.SetReleaseType(githubRelease!, eReleaseType.None);
            }
        }
    }
    
    private static RepositoryTag? FindGitHubTag(string tag)
        => _githubTags.FirstOrDefault(x => x.Name == tag);
    private static Release? FindGitHubRelease(string tag)
        => _githubReleases.FirstOrDefault(x => x.TagName == tag);

    private static UnityVersion? FindLatest()
    {
        UnityVersion? latest = null;
        foreach (var release in _githubReleases)
            if (release is { Draft: false } and { Prerelease: false })
            {
                if (!UnityVersion.TryParse(release.TagName, string.Empty, out UnityVersion releaseVersion))
                    continue;
                if ((latest == null)
                    || (latest.Value.SemVer.ComparePrecedenceTo(releaseVersion.SemVer) <= 0))
                    latest = releaseVersion;
            }
        return latest;
    }

    private static async Task<bool> TryProcess(
        PackageBase package,
        Release? release,
        UnityVersion unityVersion)
    {
        // Create Temporary Directory
        if (!Config.KeepTempFolder
            && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        if (!Directory.Exists(_tempDir))
            Directory.CreateDirectory(_tempDir);

        // Process Package
        bool success = true;
        try
        {
            if (await package.Download(unityVersion)
                && await package.Extract())
                await package.Bundle(release);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            success = false;
        }
        
        // Remove Temporary Directory
        if (!Config.KeepTempFolder
            && Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        
        Console.WriteLine();
        Console.WriteLine("------");
        Console.WriteLine();
        return success;
    }
}

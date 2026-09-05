using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Semver;

namespace Generator;

public static class UnityAPI
{
    public static async Task<IEnumerable<UnityVersion>> GetAvailableVersionsAsync(bool latestBuildsOnly = true, bool stableReleasesOnly = true)
    {
        List<UnityVersion> result = [];

        var skip = 0;
        while (true)
        {
            var resp = await GetUnityReleasesAsync(Config.UnityPageSize, skip);

            foreach (var edge in resp.Data.GetUnityReleases.Edges)
            {
                if (!UnityVersion.TryParse(edge.Node.Version, edge.Node.ShortRevision, out var unityVer))
                    continue;

                if (stableReleasesOnly && unityVer.BuildType != 'f')
                    continue;

                if (latestBuildsOnly)
                {
                    var otherIdx = result.FindIndex(x => x.Major == unityVer.Major && x.Minor == unityVer.Minor && x.Patch == unityVer.Patch && x.BuildType == unityVer.BuildType);
                    if (otherIdx != -1)
                    {
                        if (result[otherIdx].BuildNumber < unityVer.BuildNumber)
                            result[otherIdx] = unityVer;

                        continue;
                    }
                }
                
                result.Add(unityVer);
            }

            if (!resp.Data.GetUnityReleases.PageInfo.HasNextPage)
                break;

            skip += resp.Data.GetUnityReleases.Edges.Length;
        }
        
        // Return Distinct to remove Duplicates
        return result
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.SemVer,
                Comparer<SemVersion>.Create((a, b) => a.CompareSortOrderTo(b)));
    }

    private static async Task<GetUnityReleasesResponse> GetUnityReleasesAsync(int limit, int skip)
    {
        var body = new JsonObject
        {
            ["operationName"] = "GetRelease",
            ["variables"] = new JsonObject
            {
                ["limit"] = limit,
                ["skip"] = skip
            },
            ["query"] = "query GetRelease($limit: Int, $skip: Int) { getUnityReleases(limit: $limit, skip: $skip, entitlements: [XLTS]) { pageInfo { hasNextPage }, edges { node { version, shortRevision } } } }"
        };

        var resp = await HttpRequest.PostAsync(Config.UnityGraphQLApiUrl, body);

        var respString = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GetUnityReleasesResponse>(respString) ?? throw new Exception("getUnityReleases returned no content.");
    }
    
    public static string GetSetupURL(UnityVersion unityVersion, UnityPlatformID platform, Architecture architecture)
    {
        string platformPrefix = string.Empty;
        string fileName = string.Empty;

        switch (platform)
        {
            case UnityPlatformID.Linux:
                platformPrefix = "LinuxEditorInstaller";
                fileName = $"Unity-{unityVersion}.tar.xz";
                break;
                
            case UnityPlatformID.Mac:
                fileName = $"Unity-{unityVersion}.pkg";
                switch (architecture)
                {
                    case Architecture.Arm64:
                        platformPrefix = "MacEditorInstallerArm64";
                        break;
                        
                    case Architecture.X64:
                    default:
                        platformPrefix = "MacEditorInstaller";
                        break;
                }
                break;
                
            case UnityPlatformID.Windows:
            default:
                switch (architecture)
                {
                    case Architecture.Arm64:
                        platformPrefix = "WindowsArm64EditorInstaller";
                        fileName = $"UnitySetupArm64-{unityVersion}.exe";
                        break;
                        
                    case Architecture.X64:
                        platformPrefix = "Windows64EditorInstaller";
                        fileName = $"UnitySetup64-{unityVersion}.exe";
                        break;
                    
                    case Architecture.X86:
                    default:
                        platformPrefix = "WindowsEditorInstaller";
                        fileName = $"UnitySetup-{unityVersion}.exe";
                        break;
                }
                break;
        }

        return $"https://download.unity3d.com/download_unity/{unityVersion.Id}/{platformPrefix}/{fileName}";
    }
    
    public static string GetComponentURL(UnityVersion unityVersion, 
        UnityPlatformID platform, 
        UnityPlatformID supportPlatform, 
        UnityRuntimeID supportRuntime = UnityRuntimeID.NONE)
    {
        string? osPrefix = Enum.GetName(supportPlatform);
        string versionStr = unityVersion.ToString();
        
        string supportPrefix = string.Empty;
        if (supportPlatform != UnityPlatformID.Android)
            switch (supportRuntime)
            {
                case UnityRuntimeID.MONO:
                    supportPrefix = "-Mono";
                    break;
                
                case UnityRuntimeID.IL2CPP:
                    supportPrefix = "-IL2CPP";
                    break;
                
                case UnityRuntimeID.NONE:
                default:
                    break;
            }
        
        string platformPrefix;
        string fileExt;
        switch (platform)
        {
            case UnityPlatformID.Linux:
                platformPrefix = "LinuxEditorTargetInstaller";
                fileExt = ".tar.xz";
                break;
                
            case UnityPlatformID.Mac:
                platformPrefix = "MacEditorTargetInstaller";
                fileExt = ".pkg";
                break;
                
            case UnityPlatformID.UWP:
                if (unityVersion.Major >= 2019)
                {
                    osPrefix = "Universal-Windows-Platform";
                    supportPrefix = string.Empty;
                }
                goto default;
                
            case UnityPlatformID.Windows:
            default:
                platformPrefix = "TargetSupportInstaller";
                fileExt = ".exe";
                break;
        }
        
        return $"https://download.unity3d.com/download_unity/{unityVersion.Id}/{platformPrefix}/UnitySetup-{osPrefix}{supportPrefix}-Support-for-Editor-{versionStr}{fileExt}";
    }
}
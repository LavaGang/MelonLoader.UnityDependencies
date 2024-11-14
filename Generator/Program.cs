﻿using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json.Nodes;
using Octokit;
using ProductHeaderValue = Octokit.ProductHeaderValue;

namespace Generator;

internal static class Program
{
    private static string repoOwner = null!;
    private static string repoName = null!;
    private static string repoMainBranch = null!;

    private static readonly GitHubClient github = new GitHubClient(new ProductHeaderValue("MelonLoader.UnityDependencies"));
    
    private static readonly HttpClient http = new();
    private static readonly int[] majorVersions =
    [
        5,
        2017,
        2018,
        2019,
        2020,
        2021,
        2022,
        2023,
        6,
        7
    ];

    private static async Task Main(string[] args)
    {
        if (args.Length < 3)
        {
            throw new ArgumentException("The generator requires the following arguments: Repo Owner, Repo Name, Main Branch Name");
        }
        
        repoOwner = args[0];
        repoName = args[1];
        repoMainBranch = args[2];
        
        var token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (string.IsNullOrEmpty(token))
            throw new ArgumentException("No token provided; GH_TOKEN environment variable is required.");
        
        github.Credentials = new(token);
        
        http.DefaultRequestHeaders.Add("User-Agent", "Unity web player");

        Console.WriteLine("Fetching available releases");
        var versions = await GetAvailableVersionsAsync();

        Console.WriteLine("Fetching existing releases");
        var releases = await github.Repository.Release.GetAll(repoOwner, repoName);
        
        foreach (var version in versions)
        {
            if (releases.Any(x => x.TagName == version.ShortName))
                continue;

            await ProcessVersionAsync(version);
        }
    }

    private static async Task ProcessVersionAsync(UnityVersion version)
    {
        // Exclude older versions that do not have the android support bundle
        if (version.Major < 5 || version is { Major: 5, Minor: < 3 })
            return;
        
        Console.WriteLine();
        Console.WriteLine($"Processing version {version}");

        var monoBundleUrl = $"https://download.unity3d.com/download_unity/{version.Id}/MacEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-{version}.pkg";

        var tempDir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "gen.temp");
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
        Directory.CreateDirectory(tempDir);
        try
        {
            var pkgPath = Path.Combine(tempDir, "mono.pkg");
            
            Console.WriteLine("Downloading the Android Bundle");
            using (var resp = await http.GetAsync(monoBundleUrl, HttpCompletionOption.ResponseContentRead))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine("No bundle found. Skipping...");
                    return;
                }

                await using (var fileStr = File.Create(pkgPath))
                    await resp.Content.CopyToAsync(fileStr);
            }
            
            Console.WriteLine("Extracting the Payload Archive");
            await SevenZip.ExtractAsync(pkgPath, tempDir, false, "TargetSupport.pkg.tmp/Payload");
            File.Delete(pkgPath);
            
            var payloadArchPath = Path.Combine(tempDir, "Payload");
            Console.WriteLine("Extracting the Payload Archive Archive");
            await SevenZip.ExtractAsync(payloadArchPath, tempDir, false);
            File.Delete(payloadArchPath);
            
            var payloadPath = Path.Combine(tempDir, "Payload~");
            Console.WriteLine("Extracting the last one...");
            await SevenZip.ExtractAsync(payloadPath, tempDir, true, "./Variations/il2cpp/Managed/*", "./Variations/il2cpp/Release/Libs/*");
            File.Delete(payloadPath);
            
            var managedDir = Path.Combine(tempDir, "Variations", "il2cpp", "Managed");
            var libsDir = Path.Combine(tempDir, "Variations", "il2cpp", "Release", "Libs");
            
            Console.WriteLine("Bundling Managed.zip");
            using var managedZipStr = new MemoryStream();
            using (var managedZip = new ZipArchive(managedZipStr, ZipArchiveMode.Create, true))
            {
                foreach (var file in Directory.EnumerateFiles(managedDir, "*.dll"))
                {
                    managedZip.CreateEntryFromFile(file, Path.GetFileName(file));
                }
            }
            managedZipStr.Seek(0, SeekOrigin.Begin);

            Console.WriteLine("Creating a new repo tag");
            var commitSha = (await github.Repository.Branch.Get(repoOwner, repoName, repoMainBranch)).Commit.Sha;
            await github.Git.Reference.Create(repoOwner, repoName, new($"refs/tags/{version.ShortName}", commitSha));
            
            // Create a draft release, upload all the assets and undraft it
            Console.WriteLine("Creating a new repo draft release");
            var release = await github.Repository.Release.Create(repoOwner, repoName, new(version.ShortName)
            {
                Name = version.ShortName,
                Body = "Automatically generated and uploaded by the MelonLoader.UnityDependencies Generator",
                Draft = true
            });

            Console.WriteLine("Uploading Managed.zip");
            await github.Repository.Release.UploadAsset(release, new("Managed.zip", "application/zip", managedZipStr, null));

            foreach (var dir in Directory.EnumerateDirectories(libsDir))
            {
                var libunityPath = Path.Combine(dir, "libunity.so");
                if (!File.Exists(libunityPath))
                    continue;
                
                var arch = Path.GetFileName(dir);

                var assetName = $"libunity.so.{arch}";
                
                Console.WriteLine($"Uploading {assetName}");
                await using var assetStr = File.OpenRead(libunityPath);
                await github.Repository.Release.UploadAsset(release, new(assetName, "application/x-msdownload", assetStr, null));
            }
            
            // Undraft it, at which point it becomes public
            var releaseUpdate = release.ToUpdate();
            releaseUpdate.Draft = false;
            await github.Repository.Release.Edit(repoOwner, repoName, release.Id, releaseUpdate);
            
            Console.WriteLine("Done.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static async Task<IEnumerable<UnityVersion>> GetAvailableVersionsAsync(bool latestBuildsOnly = true, bool stableReleasesOnly = true)
    {
        List<UnityVersion> result = [];
        foreach (var major in majorVersions)
        {
            var body = new JsonObject
            {
                ["operationName"] = "GetRelease",
                ["query"] = "query GetRelease($limit: Int, $skip: Int, $version: String!, $stream: [UnityReleaseStream!]) {\n  getUnityReleases(\n    limit: $limit\n    skip: $skip\n    stream: $stream\n    version: $version\n    entitlements: [XLTS]\n  ) {\n    totalCount\n    edges {\n      node {\n        version\n        entitlements\n        releaseDate\n        unityHubDeepLink\n        stream\n        __typename\n      }\n      __typename\n    }\n    __typename\n  }\n}",
                ["variables"] = new JsonObject
                {
                    ["limit"] = 300,
                    ["version"] = major.ToString()
                }
            };

            var resp = await http.PostAsync("https://services.unity.com/graphql", new StringContent(body.ToJsonString(), MediaTypeHeaderValue.Parse(MediaTypeNames.Application.Json)));

            resp.EnsureSuccessStatusCode();

            var content = await resp.Content.ReadAsStringAsync();
            var edges = JsonNode.Parse(content)!["data"]!["getUnityReleases"]!["edges"]!.AsArray();

            foreach (var edge in edges)
            {
                var node = edge!["node"]!;
                var version = (string)node["version"]!;
                var hubLink = (string)node["unityHubDeepLink"]!;
                
                var lastSlashIdx = hubLink.LastIndexOf('/');
                if (lastSlashIdx == -1)
                    continue;
                
                var id = hubLink[(lastSlashIdx + 1)..];
                
                if (!UnityVersion.TryParse(version, id, out var unityVer))
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
        }

        return result;
    }
}

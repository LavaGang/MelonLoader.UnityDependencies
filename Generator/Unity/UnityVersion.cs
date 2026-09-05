using System.Text.RegularExpressions;
using Semver;

namespace Generator;

public record struct UnityVersion
{
    public required string Id { get; set; }
    public required SemVersion SemVer { get; set; }
    public required int Major { get; set; }
    public required int Minor { get; set; }
    public required int Patch { get; set; }
    public required char BuildType { get; set; }
    public required int BuildNumber { get; set; }
    
    public string ShortName => $"{Major}.{Minor}.{Patch}";
    
    public readonly override string ToString()
        => $"{Major}.{Minor}.{Patch}{BuildType}{BuildNumber}";

    public static bool TryParse(string value, string id, out UnityVersion unityVersion)
    {
        unityVersion = default;

        var match = Regex.Match(
            value,
            @"^(?<version>\d+\.\d+\.\d+)(?<type>[abcfpx])(?<revision>\d+)$");

        if (!match.Success)
            return false;

        var version = match.Groups["version"].Value;
        var type = match.Groups["type"].Value;

        if (string.IsNullOrEmpty(type))
            type = "f";

        var revision = match.Groups["revision"].Value;
        if (string.IsNullOrEmpty(revision))
            revision = "1";

        SemVersion semVer = SemVersion.Parse($"{version}-{type}.{revision}");
        unityVersion = new()
        {
            Id = id,
            SemVer = semVer,
            Major = (int)semVer.Major,
            Minor = (int)semVer.Minor,
            Patch = (int)semVer.Patch,
            BuildType = type.ToCharArray()[0],
            BuildNumber = int.Parse(revision)
        };
        
        return true;
    }
}
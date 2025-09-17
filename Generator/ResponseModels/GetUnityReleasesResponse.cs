using System.Text.Json.Serialization;

namespace Generator.ResponseModels;

public class GetUnityReleasesResponse
{
    [JsonPropertyName("data")]
    public Query Data { get; set; } = null!;

    public class Query
    {
        [JsonPropertyName("getUnityReleases")]
        public UnityReleaseOffsetConnection GetUnityReleases { get; set; } = null!;
    }

    public class UnityReleaseOffsetConnection
    {
        [JsonPropertyName("pageInfo")]
        public UnityReleaseOffsetPageInfo PageInfo { get; set; } = null!;

        [JsonPropertyName("edges")]
        public UnityReleaseOffsetEdge[] Edges { get; set; } = null!;
    }

    public class UnityReleaseOffsetEdge
    {
        [JsonPropertyName("node")]
        public UnityRelease Node { get; set; } = null!;
    }

    public class UnityRelease
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = null!;

        [JsonPropertyName("shortRevision")]
        public string ShortRevision { get; set; } = null!;
    }

    public class UnityReleaseOffsetPageInfo
    {
        [JsonPropertyName("hasNextPage")]
        public bool HasNextPage { get; set; }
    }
}

namespace Generator;

internal static class Config
{
    #region Internal Members
    
    // General
    internal static bool KeepTempFolder;

    // Web Requests
    internal static string WebRequestUserAgent = "Unity web player";
    internal static int WebRequestTimeout = 600;
    internal static bool WebRequestPrintDownloadProgress;

    // Unity
    internal static string UnityGraphQLApiUrl = "https://services.unity.com/api/live-platform/v1/graphql";
    internal static int UnityPageSize = 300;
    internal static string UnityTargetVersion = string.Empty;
    internal static bool UnityProcessAndroid;
    internal static string UnityOutputDirectory = string.Empty;
    
    // GitHub
    internal static int GitHubTimeout = 600;
    internal static string GitHubApiKey = string.Empty;
    internal static string GitHubRepo = string.Empty;
    internal static string GitHubRepoBranch = string.Empty;
    internal static bool GitHubUploadPackages;
    internal static bool GitHubUpdateExistingReleases;
    internal static bool GitHubPurgeExistingReleases;

    #endregion

    #region Internal Methods

    internal static void Load()
    {
        // General
        KeepTempFolder = GetEnvBool(nameof(KeepTempFolder), KeepTempFolder);
        
        // Web Requests
        WebRequestUserAgent = GetEnvString(nameof(WebRequestUserAgent), WebRequestUserAgent);
        WebRequestTimeout = GetEnvInt(nameof(WebRequestTimeout), WebRequestTimeout);
        WebRequestPrintDownloadProgress = GetEnvBool(nameof(WebRequestPrintDownloadProgress), WebRequestPrintDownloadProgress);
        
        // Unity
        UnityGraphQLApiUrl = GetEnvString(nameof(UnityGraphQLApiUrl), UnityGraphQLApiUrl);
        UnityPageSize = GetEnvInt(nameof(UnityPageSize), UnityPageSize);
        UnityTargetVersion = GetEnvString(nameof(UnityTargetVersion), UnityTargetVersion);
        UnityProcessAndroid = GetEnvBool(nameof(UnityProcessAndroid), UnityProcessAndroid);
        UnityOutputDirectory = GetEnvString(nameof(UnityOutputDirectory), UnityOutputDirectory);
        
        // GitHub
        GitHubTimeout = GetEnvInt(nameof(GitHubTimeout), GitHubTimeout);
        GitHubApiKey = GetEnvString(nameof(GitHubApiKey), GitHubApiKey);
        GitHubRepo = GetEnvString(nameof(GitHubRepo), GitHubRepo);
        GitHubRepoBranch = GetEnvString(nameof(GitHubRepoBranch), GitHubRepoBranch);
        GitHubUploadPackages = GetEnvBool(nameof(GitHubUploadPackages), GitHubUploadPackages);
        GitHubUpdateExistingReleases = GetEnvBool(nameof(GitHubUpdateExistingReleases), GitHubUpdateExistingReleases);
        GitHubPurgeExistingReleases = GetEnvBool(nameof(GitHubPurgeExistingReleases), GitHubPurgeExistingReleases);
        
        // Validate Values
        Validate();
    }
    
    #endregion
    
    #region Private Methods

    private static void Validate()
    {
        // Web Requests
        ValidateString(nameof(WebRequestUserAgent), WebRequestUserAgent);
        WebRequestTimeout = Math.Clamp(WebRequestTimeout, 30, WebRequestTimeout);
        
        // Unity
        ValidateString(nameof(UnityGraphQLApiUrl), UnityGraphQLApiUrl);
        GitHubTimeout = Math.Clamp(GitHubTimeout, 30, GitHubTimeout);
        
        // GitHub
        if (GitHubUploadPackages)
        {
            ValidateString(nameof(GitHubApiKey), GitHubApiKey);
            ValidateString(nameof(GitHubRepo), GitHubRepo);
            ValidateString(nameof(GitHubRepoBranch), GitHubRepoBranch);
        }
    }

    private static void ValidateString(string name, 
        string value)
    {
        if (string.IsNullOrEmpty(value)
            || string.IsNullOrWhiteSpace(value))
            throw new Exception($"{name} is not set");
    }

    private static string GetEnvString(string key, string defaultValue)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(env)
            || string.IsNullOrWhiteSpace(env))
            return defaultValue;
        return env;
    }

    private static int GetEnvInt(string key, int defaultValue)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(env)
            || string.IsNullOrWhiteSpace(env)
            || !int.TryParse(env, out int value))
            return defaultValue;
        return value;
    }

    private static bool GetEnvBool(string key, bool defaultValue)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(env)
            || string.IsNullOrWhiteSpace(env)
            || !bool.TryParse(env, out bool value))
            return defaultValue;
        return value;
    }
    
    private static T GetEnvEnumFromName<T>(string key, T defaultValue)
    {
        var env = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrEmpty(env) 
            || string.IsNullOrWhiteSpace(env))
            return defaultValue;
        return ParseEnumFromString<T>(env);
    }
    
    private static T ParseEnumFromString<T>(string value)
    {
        Array values = Enum.GetValues(typeof(T));
        string[] names = Enum.GetNames(typeof(T));
        for (int i = 0; i < values.Length; i++)
            if (value == names[i])
                return (T)values.GetValue(i)!;
        return default!;
    }

    #endregion
}

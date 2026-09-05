using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json.Nodes;

namespace Generator;

internal static class HttpRequest
{
    public delegate void ProgressEventHandler(double progress);
    
    private static HttpClient? _client;
    private static HttpClient GetClient()
    {
        if (_client == null)
        {
            _client = new HttpClient();
            _client.MaxResponseContentBufferSize = 2147483647;
            _client.Timeout = TimeSpan.FromSeconds(Config.WebRequestTimeout);
            _client.DefaultRequestHeaders.Add("User-Agent", Config.WebRequestUserAgent);
        }
        return _client;
    }
    
    public static async Task DownloadFileAsync(
        string url,
        string filePath,
        ProgressEventHandler? onProgress = null)
    {
        using HttpResponseMessage response =
            await GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();

        long? length = response.Content.Headers.ContentLength;
        if (length == null)
            return;

        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using FileStream destination = File.Create(filePath);

        byte[] buffer = new byte[64 * 1024];
        long totalRead = 0;
        double lastProgress = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer);
            if (read == 0)
                break;
            await destination.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;
            
            if (Config.WebRequestPrintDownloadProgress)
            {
                double progress = Math.Truncate((double)(totalRead * 100.0 / length));
                if (progress > lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(progress);
                }
            }
        }

        if (length.HasValue && totalRead != length.Value)
        {
            throw new IOException(
                $"Download incomplete. Expected {length.Value} bytes, got {totalRead}.");
        }
    }
    
    internal static async Task<HttpResponseMessage> GetAsync(string url, 
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        // Get Client
        HttpClient client = GetClient();
        
        // Contact
        var resp = await client.GetAsync(url, completionOption);
        resp.EnsureSuccessStatusCode();
        return resp;
    }
    
    internal static async Task<HttpResponseMessage> PostAsync(string url, 
        JsonObject? body = null)
    {
        // Get Client
        HttpClient client = GetClient();
        
        // Create Body
        StringContent content = null;
        if (body != null)
            content = new StringContent(body.ToJsonString(),
                MediaTypeHeaderValue.Parse(MediaTypeNames.Application.Json));
        
        // Contact
        var resp = await client.PostAsync(url, content);
        resp.EnsureSuccessStatusCode();
        return resp;
    }
}

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace WorldCup2026.Helpers;

/// <summary>
/// Shared HTTP client utilities for API services.
/// </summary>
public static class ApiClientHelper
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static HttpClient CreateClient(string baseUrl, int timeoutSeconds = 30)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            DefaultRequestHeaders =
            {
                { "User-Agent", "WorldCup2026-App/1.0" },
                { "Accept", "application/json" }
            }
        };
    }

    public static async Task<T?> GetAsync<T>(HttpClient client, string url, CancellationToken ct = default)
    {
        var json = await client.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public static async Task<T?> GetWithRetryAsync<T>(HttpClient client, string url, int maxRetries = 2, CancellationToken ct = default)
    {
        Exception? lastEx = null;
        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                var json = await client.GetStringAsync(url, ct);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex) when (i < maxRetries)
            {
                lastEx = ex;
                await Task.Delay(1000 * (i + 1), ct);
            }
        }
        System.Diagnostics.Debug.WriteLine($"GetWithRetry failed for {url}: {lastEx?.Message}");
        return default;
    }
}

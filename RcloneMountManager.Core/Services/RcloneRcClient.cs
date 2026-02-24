using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RcloneMountManager.Core.Services;

public sealed class RcloneRcClient
{
    private readonly HttpClient _httpClient;

    public RcloneRcClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(3);
    }

    public async Task<int?> GetPidAsync(int rcPort, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"http://localhost:{rcPort}/core/pid");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("pid", out JsonElement pidElement))
            {
                return pidElement.GetInt32();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsAliveAsync(int rcPort, CancellationToken cancellationToken)
    {
        int? pid = await GetPidAsync(rcPort, cancellationToken);
        return pid.HasValue;
    }

    public async Task<bool> QuitAsync(int rcPort, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, $"http://localhost:{rcPort}/core/quit");
            request.Content = new StringContent("{\"exitCode\":0}", Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

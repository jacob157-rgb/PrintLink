using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PrintLink.Core.Models;
using PrintLink.Core.Network;

namespace PrintLink.Network.Client;

public record ServerHealthResponse(string Status, string Version, string MachineName, string Mode, DateTimeOffset Timestamp);
public record SubmitJobResponse(Guid JobId, string Status, int SpoolerJobId, long BytesWritten, bool IsDuplicate, string? Message);

public sealed class PrintLinkClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _serverBaseUrl;
    private string? _bearerToken;

    public PrintLinkClient(string serverBaseUrl, string? bearerToken = null, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverBaseUrl);
        _serverBaseUrl = serverBaseUrl.TrimEnd('/');
        _bearerToken = bearerToken;

        _httpClient = httpClient ?? new HttpClient();
        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
    }

    public void SetToken(string token)
    {
        _bearerToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ServerHealthResponse?> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_serverBaseUrl}/api/v1/health";
        return await _httpClient.GetFromJsonAsync<ServerHealthResponse>(url, cancellationToken);
    }

    public async Task<IReadOnlyList<PrinterInfo>> GetSharedPrintersAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_serverBaseUrl}/api/v1/printers";
        var printers = await _httpClient.GetFromJsonAsync<List<PrinterInfo>>(url, cancellationToken);
        return printers ?? [];
    }

    public async Task<byte[]?> DownloadDriverPackageAsync(string printerName, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"{_serverBaseUrl}/api/v1/printers/{Uri.EscapeDataString(printerName)}/driver-package";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
        }
        catch
        {
        }
        return null;
    }

    public async Task<SubmitJobResponse> SubmitJobAsync(
        Guid jobId,
        string printerName,
        string docName,
        Stream payloadStream,
        string dataType = "RAW",
        CancellationToken cancellationToken = default)
    {
        string url = $"{_serverBaseUrl}/api/v1/jobs/submit";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-PrintLink-JobId", jobId.ToString());
        request.Headers.Add("X-PrintLink-Printer", printerName);
        request.Headers.Add("X-PrintLink-DocName", docName);
        request.Headers.Add("X-PrintLink-DataType", dataType);
        request.Content = new StreamContent(payloadStream);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SubmitJobResponse>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("Empty response received from server.");
    }

    public async Task<PrintJob?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        string url = $"{_serverBaseUrl}/api/v1/jobs/{jobId}/status";
        return await _httpClient.GetFromJsonAsync<PrintJob>(url, cancellationToken);
    }

    /// <summary>
    /// Executes an operation with exponential backoff and jitter upon transient network failures.
    /// </summary>
    public async Task<T> ExecuteWithAutoReconnectAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxRetries = 5,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxRetries - 1)
            {
                var delay = BackoffCalculator.CalculateBackoff(attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }

        // Final attempt without catching exception
        return await action(cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

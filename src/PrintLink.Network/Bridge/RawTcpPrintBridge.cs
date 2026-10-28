using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace PrintLink.Network.Bridge;

/// <summary>
/// A high-performance loopback TCP listener on 127.0.0.1 that intercepts rendered
/// print streams from Windows Spooler and submits them to a remote PrintLink server.
/// </summary>
public sealed class RawTcpPrintBridge : IAsyncDisposable
{
    private static readonly Regex PjlJobNameRegex = new(@"@PJL\s+(?:SET\s+)?JOBNAME\s*=\s*""?([^""\r\n]+)""?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PostScriptTitleRegex = new(@"%%Title:\s*([^\r\n]+)", RegexOptions.Compiled);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenerTask;

    public int Port { get; }
    public string RemotePrinterName { get; }
    public string WindowsQueueName { get; }
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Callback invoked when a complete print job stream has been received from Windows Spooler.
    /// Returns: (Success, SpoolerJobId, ErrorMessage)
    /// </summary>
    public Func<Guid, string, string, byte[], Task<(bool Success, int? SpoolerJobId, string? ErrorMessage)>>? OnSubmitJob { get; set; }

    public RawTcpPrintBridge(int port, string remotePrinterName, string windowsQueueName)
    {
        Port = port;
        RemotePrinterName = remotePrinterName;
        WindowsQueueName = windowsQueueName;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        if (IsRunning) return;

        _listener.Start();
        IsRunning = true;
        _listenerTask = Task.Run(() => AcceptConnectionsAsync(_cts.Token));
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Continue accepting other connections
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var ms = new MemoryStream();

            byte[] buffer = new byte[65536]; // 64 KB read buffer
            int bytesRead;

            try
            {
                // Read until the Windows Spooler closes the TCP connection (indicating end of job)
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    ms.Write(buffer, 0, bytesRead);
                }
            }
            catch (Exception)
            {
                // Connection terminated or cancelled
            }

            if (ms.Length == 0) return;

            byte[] jobData = ms.ToArray();
            Guid jobId = Guid.NewGuid();
            string documentTitle = ExtractDocumentTitle(jobData);

            if (OnSubmitJob != null)
            {
                try
                {
                    await OnSubmitJob.Invoke(jobId, RemotePrinterName, documentTitle, jobData);
                }
                catch
                {
                    // Error submitted to tracker or repository
                }
            }
        }
    }

    /// <summary>
    /// Attempts to extract the original document title from PJL or PostScript comments.
    /// </summary>
    public static string ExtractDocumentTitle(byte[] data)
    {
        try
        {
            int previewLength = Math.Min(data.Length, 4096);
            string headerText = Encoding.ASCII.GetString(data, 0, previewLength);

            var pjlMatch = PjlJobNameRegex.Match(headerText);
            if (pjlMatch.Success && !string.IsNullOrWhiteSpace(pjlMatch.Groups[1].Value))
            {
                return pjlMatch.Groups[1].Value.Trim();
            }

            var psMatch = PostScriptTitleRegex.Match(headerText);
            if (psMatch.Success && !string.IsNullOrWhiteSpace(psMatch.Groups[1].Value))
            {
                return psMatch.Groups[1].Value.Trim();
            }
        }
        catch
        {
        }

        return $"PrintJob_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        IsRunning = false;
        _cts.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        if (_listenerTask != null)
        {
            try
            {
                await _listenerTask;
            }
            catch
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }
}

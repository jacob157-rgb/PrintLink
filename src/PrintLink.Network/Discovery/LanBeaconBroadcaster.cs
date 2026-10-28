using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrintLink.Core.Interfaces;

namespace PrintLink.Network.Discovery;

/// <summary>
/// Background UDP beacon broadcaster and discover request responder.
/// Allows client machines on the local network to discover this PrintLink server automatically.
/// </summary>
public sealed class LanBeaconBroadcaster : IAsyncDisposable
{
    public const int DiscoveryPort = 8271;
    public const string DiscoverMagic = "PRINTLINK_DISCOVER";
    public const string ProtocolId = "PrintLink-v1";

    private readonly int _serverPort;
    private readonly string _operatingMode;
    private readonly IPrinterEnumerator _printerEnumerator;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();

    private UdpClient? _udpClient;
    private Task? _broadcastTask;
    private Task? _listenerTask;

    public LanBeaconBroadcaster(
        int serverPort,
        string operatingMode,
        IPrinterEnumerator printerEnumerator,
        ILogger? logger = null)
    {
        _serverPort = serverPort;
        _operatingMode = operatingMode;
        _printerEnumerator = printerEnumerator;
        _logger = logger;
    }

    public void Start(int intervalSeconds = 5)
    {
        try
        {
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.EnableBroadcast = true;
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            _broadcastTask = Task.Run(() => BroadcastLoopAsync(intervalSeconds, _cts.Token));
            _listenerTask = Task.Run(() => ListenerLoopAsync(_cts.Token));

            _logger?.LogInformation("LanBeaconBroadcaster started on UDP port {Port}", DiscoveryPort);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start UDP beacon broadcaster on port {Port}", DiscoveryPort);
        }
    }

    private byte[] CreateBeaconBytes()
    {
        var sharedPrinters = new List<DiscoveredPrinterSummary>();
        try
        {
            sharedPrinters = _printerEnumerator.GetPrinters()
                .Where(p => p.IsShared)
                .Select(p => new DiscoveredPrinterSummary(p.Name, p.DisplayName, p.DriverName, p.StatusDescription, p.CapabilitiesSummary, p.CustomForms))
                .ToList();
        }
        catch
        {
        }

        var payload = new LanBeaconPayload(
            Protocol: ProtocolId,
            MachineName: Environment.MachineName,
            Port: _serverPort,
            OperatingMode: _operatingMode,
            PrinterCount: sharedPrinters.Count,
            Version: "1.0.0",
            Timestamp: DateTimeOffset.UtcNow,
            SharedPrinters: sharedPrinters);

        string json = JsonSerializer.Serialize(payload);
        return Encoding.UTF8.GetBytes(json);
    }

    private async Task BroadcastLoopAsync(int intervalSeconds, CancellationToken token)
    {
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient != null)
                {
                    byte[] bytes = CreateBeaconBytes();
                    foreach (var addr in LanDiscoveryClient.GetBroadcastAddresses())
                    {
                        try
                        {
                            var target = new IPEndPoint(addr, DiscoveryPort);
                            await _udpClient.SendAsync(bytes, bytes.Length, target);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogDebug("Broadcast send exception: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_udpClient == null) break;

                var result = await _udpClient.ReceiveAsync(token);
                string message = Encoding.UTF8.GetString(result.Buffer).Trim();

                if (message.StartsWith(DiscoverMagic, StringComparison.OrdinalIgnoreCase))
                {
                    byte[] responseBytes = CreateBeaconBytes();
                    await _udpClient.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogDebug("UDP Listener receive exception: {Message}", ex.Message);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            _udpClient?.Close();
            _udpClient?.Dispose();
        }
        catch
        {
        }

        if (_broadcastTask != null)
        {
            try { await _broadcastTask; } catch { }
        }

        if (_listenerTask != null)
        {
            try { await _listenerTask; } catch { }
        }

        _cts.Dispose();
    }
}

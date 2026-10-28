using System.Collections.Concurrent;
using System.Net.NetworkInformation;

namespace PrintLink.Network.Bridge;

/// <summary>
/// Coordinates multiple local TCP print bridges for installed remote printers.
/// </summary>
public sealed class PrintBridgeManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, RawTcpPrintBridge> _bridges = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<RawTcpPrintBridge> ActiveBridges => _bridges.Values.ToList();

    /// <summary>
    /// Finds an available TCP port for the local bridge, starting from 9100.
    /// </summary>
    public static int AllocateAvailablePort(int startPort = 9100, int endPort = 9199)
    {
        var activeTcpListeners = IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(ep => ep.Port)
            .ToHashSet();

        for (int port = startPort; port <= endPort; port++)
        {
            if (!activeTcpListeners.Contains(port))
            {
                return port;
            }
        }

        return startPort;
    }

    /// <summary>
    /// Starts a bridge for the specified Windows queue and remote printer.
    /// </summary>
    public async Task<RawTcpPrintBridge> StartBridgeAsync(
        string remotePrinterName,
        string windowsQueueName,
        int port,
        Func<Guid, string, string, byte[], Task<(bool Success, int? SpoolerJobId, string? ErrorMessage)>> submitHandler)
    {
        // Stop existing bridge on same queue if any
        if (_bridges.TryRemove(windowsQueueName, out var existing))
        {
            await existing.DisposeAsync();
        }

        var bridge = new RawTcpPrintBridge(port, remotePrinterName, windowsQueueName)
        {
            OnSubmitJob = submitHandler
        };

        bridge.Start();
        _bridges[windowsQueueName] = bridge;
        return bridge;
    }

    /// <summary>
    /// Stops the bridge associated with the given queue.
    /// </summary>
    public async Task StopBridgeAsync(string windowsQueueName)
    {
        if (_bridges.TryRemove(windowsQueueName, out var bridge))
        {
            await bridge.DisposeAsync();
        }
    }

    public bool IsBridgeRunning(string windowsQueueName)
    {
        return _bridges.TryGetValue(windowsQueueName, out var bridge) && bridge.IsRunning;
    }

    public int? GetBridgePort(string windowsQueueName)
    {
        return _bridges.TryGetValue(windowsQueueName, out var bridge) ? bridge.Port : null;
    }

    public async Task StopAllAsync()
    {
        foreach (var bridge in _bridges.Values)
        {
            await bridge.DisposeAsync();
        }
        _bridges.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
    }
}

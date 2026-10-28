using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PrintLink.Network.Discovery;

/// <summary>
/// Client engine for discovering active PrintLink servers on the local area network.
/// </summary>
public static class LanDiscoveryClient
{
    public const int DiscoveryPort = 8271;
    public const string DiscoverMagic = "PRINTLINK_DISCOVER";
    public const string ProtocolId = "PrintLink-v1";

    /// <summary>
    /// Actively scans the local subnet for running PrintLink servers and returns discovered instances.
    /// </summary>
    /// <param name="timeout">Scan duration to listen for replies.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<DiscoveredServer>> DiscoverServersAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var discovered = new Dictionary<string, DiscoveredServer>(StringComparer.OrdinalIgnoreCase);

        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

        // Bind to any available local ephemeral port
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        byte[] probeBytes = Encoding.UTF8.GetBytes(DiscoverMagic);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            foreach (var addr in GetBroadcastAddresses())
            {
                try
                {
                    var target = new IPEndPoint(addr, DiscoveryPort);
                    await udpClient.SendAsync(probeBytes, probeBytes.Length, target);
                }
                catch
                {
                }
            }
        }
        catch
        {
            return Array.Empty<DiscoveredServer>();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var receiveTask = udpClient.ReceiveAsync(cts.Token);
                var result = await receiveTask;
                long elapsed = stopwatch.ElapsedMilliseconds;

                string hostIp = result.RemoteEndPoint.Address.ToString();
                // Normalize IPv6 loopback / any
                if (hostIp == "::1" || hostIp == "127.0.0.1")
                {
                    hostIp = "127.0.0.1";
                }

                try
                {
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    var payload = JsonSerializer.Deserialize<LanBeaconPayload>(json);

                    if (payload != null && payload.Protocol == ProtocolId)
                    {
                        string key = $"{hostIp}:{payload.Port}";
                        if (!discovered.ContainsKey(key))
                        {
                            discovered[key] = new DiscoveredServer(
                                HostAddress: hostIp,
                                Port: payload.Port,
                                ServerName: payload.MachineName,
                                OperatingMode: payload.OperatingMode,
                                SharedPrinterCount: payload.PrinterCount,
                                Version: payload.Version,
                                LatencyMs: elapsed,
                                SharedPrinters: payload.SharedPrinters);
                        }
                    }
                }
                catch
                {
                    // Ignore malformed packets from other network services
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout finishes
        }
        catch
        {
            // Socket or network teardown
        }

        return discovered.Values.ToList();
    }

    /// <summary>
    /// Performs direct printer-level auto-discovery across the local network,
    /// returning active shared printers found on all PrintLink servers.
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredNetworkPrinter>> DiscoverPrintersAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var discoveredServers = await DiscoverServersAsync(timeout, cancellationToken);
        var printerList = new List<DiscoveredNetworkPrinter>();
        var seenPrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in discoveredServers)
        {
            if (server.SharedPrinters != null && server.SharedPrinters.Count > 0)
            {
                foreach (var p in server.SharedPrinters)
                {
                    string uniqueKey = $"{server.HostAddress}:{server.Port}/{p.DisplayName}";
                    if (seenPrinters.Add(uniqueKey))
                    {
                        printerList.Add(new DiscoveredNetworkPrinter(
                            HostAddress: server.HostAddress,
                            Port: server.Port,
                            ServerName: server.ServerName,
                            PrinterName: p.Name,
                            DisplayName: p.DisplayName,
                            DriverName: p.DriverName,
                            Status: p.Status ?? "Ready",
                            IsShared: true,
                            CapabilitiesSummary: p.CapabilitiesSummary,
                            CustomForms: p.CustomForms));
                    }
                }
            }
        }

        return printerList;
    }

    /// <summary>
    /// Returns the global broadcast address and subnet-directed broadcast addresses for all active IPv4 interfaces.
    /// </summary>
    public static IEnumerable<IPAddress> GetBroadcastAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast };

        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    iface.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = iface.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null)
                    {
                        byte[] ipBytes = unicast.Address.GetAddressBytes();
                        byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                        if (ipBytes.Length == 4 && maskBytes.Length == 4)
                        {
                            byte[] broadcastBytes = new byte[4];
                            for (int i = 0; i < 4; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }
                            addresses.Add(new IPAddress(broadcastBytes));
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return addresses;
    }
}

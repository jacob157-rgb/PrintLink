using PrintLink.Core.Models;

namespace PrintLink.Network.Discovery;

/// <summary>
/// Information about a discovered PrintLink server on the local network.
/// </summary>
public sealed record DiscoveredServer(
    string HostAddress,
    int Port,
    string ServerName,
    string OperatingMode,
    int SharedPrinterCount,
    string Version,
    long LatencyMs,
    IReadOnlyList<DiscoveredPrinterSummary>? SharedPrinters = null)
{
    public string DisplayEndpoint => $"{HostAddress}:{Port}";
    public string DisplayTitle => $"{ServerName} ({HostAddress})";
}

/// <summary>
/// Information about a printer discovered directly on a remote PrintLink server.
/// </summary>
public sealed record DiscoveredNetworkPrinter(
    string HostAddress,
    int Port,
    string ServerName,
    string PrinterName,
    string DisplayName,
    string? DriverName,
    string Status,
    bool IsShared = true,
    string? CapabilitiesSummary = null,
    IReadOnlyList<CustomPageForm>? CustomForms = null);


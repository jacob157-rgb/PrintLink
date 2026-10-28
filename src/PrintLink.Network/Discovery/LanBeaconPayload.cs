using PrintLink.Core.Models;

namespace PrintLink.Network.Discovery;

/// <summary>
/// Summary descriptor of a shared printer included in the LAN beacon payload.
/// </summary>
public sealed record DiscoveredPrinterSummary(
    string Name,
    string DisplayName,
    string? DriverName = null,
    string? Status = null,
    string? CapabilitiesSummary = null,
    IReadOnlyList<CustomPageForm>? CustomForms = null);

/// <summary>
/// Broadcast discovery payload transmitted by PrintLink servers on the local network.
/// </summary>
public sealed record LanBeaconPayload(
    string Protocol,
    string MachineName,
    int Port,
    string OperatingMode,
    int PrinterCount,
    string Version,
    DateTimeOffset Timestamp,
    IReadOnlyList<DiscoveredPrinterSummary>? SharedPrinters = null);

using PrintLink.Core.Enums;

namespace PrintLink.Core.Models;

/// <summary>
/// Detailed metadata representing a locally installed or remote shared printer.
/// </summary>
public record PrinterInfo
{
    public required string Name { get; init; }
    public string? DriverName { get; init; }
    public string? PortName { get; init; }
    public string? ShareName { get; init; }
    public string? Comment { get; init; }
    public string? Location { get; init; }
    public bool IsDefault { get; init; }
    public bool IsShared { get; init; }
    public uint Attributes { get; init; }
    public PrinterStatusFlags Status { get; init; }
    public int QueuedJobCount { get; init; }
    public bool SupportsColor { get; init; } = true;
    public bool SupportsDuplex { get; init; } = false;
    public IReadOnlyList<string> SupportedPaperSizes { get; init; } = [];
    public IReadOnlyList<CustomPageForm> CustomForms { get; init; } = [];
    public bool HasDriverPackage { get; init; } = false;

    /// <summary>
    /// Friendly display name for the printer (ShareName if present and enabled, else Name).
    /// </summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(ShareName) ? ShareName : Name;

    /// <summary>
    /// Returns a human-friendly description of the printer's current operational status.
    /// </summary>
    public string StatusDescription
    {
        get
        {
            if (Status == PrinterStatusFlags.Ready)
            {
                return "Ready";
            }

            var parts = new List<string>();
            foreach (PrinterStatusFlags flag in Enum.GetValues<PrinterStatusFlags>())
            {
                if (flag != PrinterStatusFlags.Ready && Status.HasFlag(flag))
                {
                    parts.Add(flag.ToString());
                }
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "Unknown";
        }
    }

    public string CapabilitiesSummary => $"{(SupportsColor ? "Color" : "Monochrome")} • Duplex: {(SupportsDuplex ? "Supported" : "No")} • {(SupportedPaperSizes.Count > 0 ? $"{SupportedPaperSizes.Count} Formats" : "Standard")}";
}


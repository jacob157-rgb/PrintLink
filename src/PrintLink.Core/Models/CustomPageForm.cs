namespace PrintLink.Core.Models;

/// <summary>
/// Represents a custom or standardized paper/page form dimension in millimeters.
/// Used to synchronize custom forms (surat jalan, receipt, label sizes) across Server and Client.
/// </summary>
public record CustomPageForm
{
    public required string Name { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public bool IsCustom { get; init; }

    /// <summary>
    /// Formatted display string e.g. "USER (105.6 x 63.5 mm)"
    /// </summary>
    public string DisplaySize => $"{WidthMm:0.#} × {HeightMm:0.#} mm";
}

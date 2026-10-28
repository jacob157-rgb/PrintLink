using PrintLink.Core.Models;

namespace PrintLink.Core.Interfaces;

/// <summary>
/// Contract for enumerating printers available to the host operating system.
/// </summary>
public interface IPrinterEnumerator
{
    /// <summary>
    /// Retrieves all locally installed printers and active connections.
    /// </summary>
    IReadOnlyList<PrinterInfo> GetPrinters();

    /// <summary>
    /// Retrieves detailed status and configuration for a specific named printer.
    /// </summary>
    PrinterInfo? GetPrinter(string printerName);
}

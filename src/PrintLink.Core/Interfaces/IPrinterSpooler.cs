using PrintLink.Core.Models;

namespace PrintLink.Core.Interfaces;

/// <summary>
/// Contract for directly dispatching print jobs into the print subsystem.
/// </summary>
public interface IPrinterSpooler
{
    /// <summary>
    /// Synchronously writes a byte array to the specified printer via local spooler APIs.
    /// </summary>
    /// <param name="printerName">Exact Windows printer name.</param>
    /// <param name="documentTitle">Document title displayed in print queue.</param>
    /// <param name="data">Raw byte array to send (PDF, XPS, or ESC/POS).</param>
    /// <param name="dataType">Data type format (e.g. "RAW", "XPS_PASS").</param>
    SpoolResult PrintBytes(string printerName, string documentTitle, byte[] data, string dataType = "RAW");

    /// <summary>
    /// Asynchronously streams document data directly into the spooler without buffering the entire payload in memory.
    /// </summary>
    /// <param name="printerName">Exact Windows printer name.</param>
    /// <param name="documentTitle">Document title displayed in print queue.</param>
    /// <param name="dataStream">Readable data stream.</param>
    /// <param name="dataType">Data type format (e.g. "RAW", "XPS_PASS").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SpoolResult> PrintStreamAsync(
        string printerName,
        string documentTitle,
        Stream dataStream,
        string dataType = "RAW",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active or pending print job in the spooler queue.
    /// </summary>
    bool CancelJob(string printerName, int spoolerJobId);

    /// <summary>
    /// Pauses an active or pending print job in the spooler queue.
    /// </summary>
    bool PauseJob(string printerName, int spoolerJobId);

    /// <summary>
    /// Resumes a paused print job in the spooler queue.
    /// </summary>
    bool ResumeJob(string printerName, int spoolerJobId);
}

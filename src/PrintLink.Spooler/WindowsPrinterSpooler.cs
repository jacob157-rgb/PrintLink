using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Spooler.Native;

namespace PrintLink.Spooler;

/// <summary>
/// Production implementation of IPrinterSpooler using native winspool.drv APIs.
/// Directly interfaces with the Windows Print Spooler (spoolsv.exe) without requiring third-party drivers.
/// </summary>
public sealed class WindowsPrinterSpooler : IPrinterSpooler
{
    private const int DefaultBufferSize = 64 * 1024; // 64 KB streaming buffer

    public SpoolResult PrintBytes(string printerName, string documentTitle, byte[] data, string dataType = "RAW")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ArgumentNullException.ThrowIfNull(data);

        using var memoryStream = new MemoryStream(data, writable: false);
        return PrintStreamInternalAsync(printerName, documentTitle, memoryStream, dataType, CancellationToken.None).GetAwaiter().GetResult();
    }

    public Task<SpoolResult> PrintStreamAsync(
        string printerName,
        string documentTitle,
        Stream dataStream,
        string dataType = "RAW",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ArgumentNullException.ThrowIfNull(dataStream);

        return PrintStreamInternalAsync(printerName, documentTitle, dataStream, dataType, cancellationToken);
    }

    private static async Task<SpoolResult> PrintStreamInternalAsync(
        string printerName,
        string documentTitle,
        Stream dataStream,
        string dataType,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.OpenPrinter(printerName, out SafePrinterHandle hPrinter, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            return SpoolResult.Failed(err, $"Failed to open printer '{printerName}': {new Win32Exception(err).Message}");
        }

        using (hPrinter)
        {
            var docInfo = new DOC_INFO_1
            {
                pDocName = documentTitle,
                pOutputFile = null,
                pDataType = dataType
            };

            int spoolerJobId = NativeMethods.StartDocPrinter(hPrinter, 1, ref docInfo);
            if (spoolerJobId <= 0)
            {
                int err = Marshal.GetLastWin32Error();
                return SpoolResult.Failed(err, $"StartDocPrinter failed: {new Win32Exception(err).Message}");
            }

            bool docStarted = true;
            bool pageStarted = false;
            long totalBytesWritten = 0;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
            try
            {
                if (!NativeMethods.StartPagePrinter(hPrinter))
                {
                    int err = Marshal.GetLastWin32Error();
                    return SpoolResult.Failed(err, $"StartPagePrinter failed: {new Win32Exception(err).Message}", spoolerJobId);
                }
                pageStarted = true;

                int bytesRead;
                while ((bytesRead = await dataStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    IntPtr pUnmanaged = Marshal.AllocHGlobal(bytesRead);
                    try
                    {
                        Marshal.Copy(buffer, 0, pUnmanaged, bytesRead);
                        if (!NativeMethods.WritePrinter(hPrinter, pUnmanaged, bytesRead, out int written))
                        {
                            int err = Marshal.GetLastWin32Error();
                            return SpoolResult.Failed(err, $"WritePrinter failed: {new Win32Exception(err).Message}", spoolerJobId, totalBytesWritten);
                        }
                        totalBytesWritten += written;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pUnmanaged);
                    }
                }

                return SpoolResult.Succeeded(spoolerJobId, totalBytesWritten);
            }
            catch (OperationCanceledException)
            {
                return SpoolResult.Failed(9999, "Print operation was cancelled by caller.", spoolerJobId, totalBytesWritten);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);

                if (pageStarted)
                {
                    NativeMethods.EndPagePrinter(hPrinter);
                }

                if (docStarted)
                {
                    NativeMethods.EndDocPrinter(hPrinter);
                }
            }
        }
    }

    public bool CancelJob(string printerName, int spoolerJobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        if (spoolerJobId <= 0) return false;

        if (!NativeMethods.OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
        {
            return false;
        }

        using (hPrinter)
        {
            return NativeMethods.SetJob(hPrinter, (uint)spoolerJobId, 0, IntPtr.Zero, NativeMethods.JOB_CONTROL_CANCEL);
        }
    }

    public bool PauseJob(string printerName, int spoolerJobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        if (spoolerJobId <= 0) return false;

        if (!NativeMethods.OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
        {
            return false;
        }

        using (hPrinter)
        {
            return NativeMethods.SetJob(hPrinter, (uint)spoolerJobId, 0, IntPtr.Zero, NativeMethods.JOB_CONTROL_PAUSE);
        }
    }

    public bool ResumeJob(string printerName, int spoolerJobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        if (spoolerJobId <= 0) return false;

        if (!NativeMethods.OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
        {
            return false;
        }

        using (hPrinter)
        {
            return NativeMethods.SetJob(hPrinter, (uint)spoolerJobId, 0, IntPtr.Zero, NativeMethods.JOB_CONTROL_RESUME);
        }
    }
}

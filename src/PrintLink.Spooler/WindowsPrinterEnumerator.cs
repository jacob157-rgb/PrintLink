using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using PrintLink.Core.Enums;
using PrintLink.Core.Interfaces;
using PrintLink.Core.Models;
using PrintLink.Spooler.Native;

namespace PrintLink.Spooler;

/// <summary>
/// Production implementation of IPrinterEnumerator using Windows native winspool.drv APIs.
/// </summary>
public sealed class WindowsPrinterEnumerator : IPrinterEnumerator
{
    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        var printers = new List<PrinterInfo>();
        string? defaultPrinterName = TryGetDefaultPrinter();

        // Level 2 enumeration: includes Driver, Port, ShareName, Attributes, Status, and cJobs
        const uint flags = NativeMethods.PRINTER_ENUM_LOCAL | NativeMethods.PRINTER_ENUM_CONNECTIONS;
        const uint level = 2;

        NativeMethods.EnumPrinters(flags, null, level, IntPtr.Zero, 0, out uint bytesNeeded, out _);
        int lastError = Marshal.GetLastWin32Error();

        if (lastError == NativeMethods.ERROR_INSUFFICIENT_BUFFER && bytesNeeded > 0)
        {
            IntPtr pBuffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (NativeMethods.EnumPrinters(flags, null, level, pBuffer, bytesNeeded, out _, out uint count))
                {
                    int structSize = Marshal.SizeOf<PRINTER_INFO_2>();
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr currentPtr = IntPtr.Add(pBuffer, i * structSize);
                        var info = Marshal.PtrToStructure<PRINTER_INFO_2>(currentPtr);

                        if (string.IsNullOrWhiteSpace(info.pPrinterName))
                        {
                            continue;
                        }

                        bool isDefault = string.Equals(info.pPrinterName, defaultPrinterName, StringComparison.OrdinalIgnoreCase)
                                         || (info.Attributes & NativeMethods.PRINTER_ATTRIBUTE_DEFAULT) != 0;

                        bool isShared = (info.Attributes & NativeMethods.PRINTER_ATTRIBUTE_SHARED) != 0;
                        bool supportsColor = QuerySupportsColor(info.pPrinterName, info.pPortName);
                        bool supportsDuplex = QuerySupportsDuplex(info.pPrinterName, info.pPortName);
                        var paperSizes = QuerySupportedPaperSizes(info.pPrinterName, info.pPortName);
                        var customForms = QueryPrinterForms(info.pPrinterName, info.pPortName);
                        bool hasDriver = !string.IsNullOrWhiteSpace(info.pDriverName);

                        printers.Add(new PrinterInfo
                        {
                            Name = info.pPrinterName,
                            DriverName = info.pDriverName,
                            PortName = info.pPortName,
                            ShareName = isShared ? info.pShareName : null,
                            Comment = info.pComment,
                            Location = info.pLocation,
                            IsDefault = isDefault,
                            IsShared = isShared,
                            Attributes = info.Attributes,
                            Status = (PrinterStatusFlags)info.Status,
                            QueuedJobCount = (int)info.cJobs,
                            SupportsColor = supportsColor,
                            SupportsDuplex = supportsDuplex,
                            SupportedPaperSizes = paperSizes,
                            CustomForms = customForms,
                            HasDriverPackage = hasDriver
                        });
                    }

                    return printers;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
        }

        // Fallback: Level 4 enumeration (minimal attributes, higher permission tolerance)
        return EnumeratePrintersLevel4(defaultPrinterName);
    }

    public PrinterInfo? GetPrinter(string printerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        var all = GetPrinters();
        return all.FirstOrDefault(p => string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<PrinterInfo> EnumeratePrintersLevel4(string? defaultPrinterName)
    {
        var printers = new List<PrinterInfo>();
        const uint flags = NativeMethods.PRINTER_ENUM_LOCAL | NativeMethods.PRINTER_ENUM_CONNECTIONS;
        const uint level = 4;

        NativeMethods.EnumPrinters(flags, null, level, IntPtr.Zero, 0, out uint bytesNeeded, out _);
        if (bytesNeeded > 0)
        {
            IntPtr pBuffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (NativeMethods.EnumPrinters(flags, null, level, pBuffer, bytesNeeded, out _, out uint count))
                {
                    int structSize = Marshal.SizeOf<PRINTER_INFO_4>();
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr currentPtr = IntPtr.Add(pBuffer, i * structSize);
                        var info = Marshal.PtrToStructure<PRINTER_INFO_4>(currentPtr);

                        if (string.IsNullOrWhiteSpace(info.pPrinterName))
                        {
                            continue;
                        }

                        bool isDefault = string.Equals(info.pPrinterName, defaultPrinterName, StringComparison.OrdinalIgnoreCase);

                        printers.Add(new PrinterInfo
                        {
                            Name = info.pPrinterName,
                            IsDefault = isDefault,
                            Attributes = info.Attributes,
                            Status = PrinterStatusFlags.Ready
                        });
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
        }

        return printers;
    }

    private static string? TryGetDefaultPrinter()
    {
        uint bufferSize = 0;
        NativeMethods.GetDefaultPrinter(null, ref bufferSize);
        if (bufferSize > 0)
        {
            var sb = new StringBuilder((int)bufferSize);
            if (NativeMethods.GetDefaultPrinter(sb, ref bufferSize))
            {
                return sb.ToString();
            }
        }
        return null;
    }

    private static bool QuerySupportsColor(string printerName, string? portName)
    {
        try
        {
            int result = NativeMethods.DeviceCapabilities(printerName, portName, NativeMethods.DC_COLORDEVICE, IntPtr.Zero, IntPtr.Zero);
            return result == 1;
        }
        catch
        {
            return true;
        }
    }

    private static bool QuerySupportsDuplex(string printerName, string? portName)
    {
        try
        {
            int result = NativeMethods.DeviceCapabilities(printerName, portName, NativeMethods.DC_DUPLEX, IntPtr.Zero, IntPtr.Zero);
            return result == 1;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> QuerySupportedPaperSizes(string printerName, string? portName)
    {
        try
        {
            int paperCount = NativeMethods.DeviceCapabilities(printerName, portName, NativeMethods.DC_PAPERNAMES, IntPtr.Zero, IntPtr.Zero);
            if (paperCount <= 0 || paperCount > 256) return [];

            int bufferSize = paperCount * 64 * sizeof(char);
            IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int returned = NativeMethods.DeviceCapabilities(printerName, portName, NativeMethods.DC_PAPERNAMES, pBuffer, IntPtr.Zero);
                if (returned <= 0) return [];

                var paperNames = new List<string>(returned);
                for (int i = 0; i < returned; i++)
                {
                    IntPtr itemPtr = IntPtr.Add(pBuffer, i * 64 * sizeof(char));
                    string? name = Marshal.PtrToStringUni(itemPtr, 64)?.TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(name) && !paperNames.Contains(name))
                    {
                        paperNames.Add(name);
                    }
                }
                return paperNames;
            }
            finally
            {
                Marshal.FreeHGlobal(pBuffer);
            }
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<CustomPageForm> QueryPrinterForms(string printerName, string? portName)
    {
        var forms = new List<CustomPageForm>();
        try
        {
            int paperCount = NativeMethods.DeviceCapabilities(printerName, portName, (ushort)NativeMethods.DC_PAPERNAMES, IntPtr.Zero, IntPtr.Zero);
            if (paperCount > 0 && paperCount <= 256)
            {
                int nameBufSize = paperCount * 64 * sizeof(char);
                int sizeBufSize = paperCount * 8;
                IntPtr pNames = Marshal.AllocHGlobal(nameBufSize);
                IntPtr pSizes = Marshal.AllocHGlobal(sizeBufSize);
                try
                {
                    int retNames = NativeMethods.DeviceCapabilities(printerName, portName, (ushort)NativeMethods.DC_PAPERNAMES, pNames, IntPtr.Zero);
                    int retSizes = NativeMethods.DeviceCapabilities(printerName, portName, NativeMethods.DC_PAPERSIZE, pSizes, IntPtr.Zero);
                    if (retNames > 0 && retSizes > 0)
                    {
                        int limit = Math.Min(retNames, retSizes);
                        for (int i = 0; i < limit; i++)
                        {
                            IntPtr itemPtr = IntPtr.Add(pNames, i * 64 * sizeof(char));
                            string? name = Marshal.PtrToStringUni(itemPtr, 64)?.TrimEnd('\0', ' ');
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            int rawW = Marshal.ReadInt32(IntPtr.Add(pSizes, i * 8));
                            int rawH = Marshal.ReadInt32(IntPtr.Add(pSizes, i * 8 + 4));

                            double widthMm = rawW / 10.0;
                            double heightMm = rawH / 10.0;

                            bool isCustom = name.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("custom", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("x", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("continuous", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("surat", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("struk", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("receipt", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("roll", StringComparison.OrdinalIgnoreCase);

                            if (!forms.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                            {
                                forms.Add(new CustomPageForm
                                {
                                    Name = name,
                                    WidthMm = Math.Round(widthMm, 1),
                                    HeightMm = Math.Round(heightMm, 1),
                                    IsCustom = isCustom
                                });
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pNames);
                    Marshal.FreeHGlobal(pSizes);
                }
            }

            var spoolerForms = QueryServerUserForms();
            foreach (var sf in spoolerForms)
            {
                if (!forms.Any(f => string.Equals(f.Name, sf.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    forms.Add(sf);
                }
            }
        }
        catch
        {
        }

        return forms;
    }

    private static IReadOnlyList<CustomPageForm> QueryServerUserForms()
    {
        var list = new List<CustomPageForm>();
        try
        {
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = NativeMethods.SERVER_ACCESS_ENUMERATE };
            if (NativeMethods.OpenPrinterServer(null, out var hServer, ref defaults) && !hServer.IsInvalid)
            {
                using (hServer)
                {
                    NativeMethods.EnumForms(hServer, 1, IntPtr.Zero, 0, out uint needed, out _);
                    if (needed > 0)
                    {
                        IntPtr pBuffer = Marshal.AllocHGlobal((int)needed);
                        try
                        {
                            if (NativeMethods.EnumForms(hServer, 1, pBuffer, needed, out _, out uint count))
                            {
                                int structSize = Marshal.SizeOf<FORM_INFO_1>();
                                for (int i = 0; i < count; i++)
                                {
                                    IntPtr current = IntPtr.Add(pBuffer, i * structSize);
                                    var fi = Marshal.PtrToStructure<FORM_INFO_1>(current);
                                    if (fi.Flags == NativeMethods.FORM_USER && !string.IsNullOrWhiteSpace(fi.pName))
                                    {
                                        list.Add(new CustomPageForm
                                        {
                                            Name = fi.pName,
                                            WidthMm = Math.Round(fi.Size.cx / 1000.0, 1),
                                            HeightMm = Math.Round(fi.Size.cy / 1000.0, 1),
                                            IsCustom = true
                                        });
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(pBuffer);
                        }
                    }
                }
            }
        }
        catch
        {
        }
        return list;
    }
}

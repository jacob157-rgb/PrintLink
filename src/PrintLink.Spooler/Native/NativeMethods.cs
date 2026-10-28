using System.Runtime.InteropServices;

namespace PrintLink.Spooler.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DOC_INFO_1
{
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? pDocName;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string? pOutputFile;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string? pDataType;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct PRINTER_INFO_2
{
    [MarshalAs(UnmanagedType.LPWStr)] public string? pServerName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pPrinterName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pShareName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pPortName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pDriverName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pComment;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pLocation;
    public IntPtr pDevMode;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pSepFile;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pPrintProcessor;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pDatatype;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pParameters;
    public IntPtr pSecurityDescriptor;
    public uint Attributes;
    public uint Priority;
    public uint DefaultPriority;
    public uint StartTime;
    public uint UntilTime;
    public uint Status;
    public uint cJobs;
    public uint AveragePPM;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZEL
{
    public int cx;
    public int cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECTL
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct FORM_INFO_1
{
    public uint Flags;
    [MarshalAs(UnmanagedType.LPWStr)] public string pName;
    public SIZEL Size;
    public RECTL ImageableArea;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PRINTER_DEFAULTS
{
    public IntPtr pDatatype;
    public IntPtr pDevMode;
    public uint DesiredAccess;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct PRINTER_INFO_4
{
    [MarshalAs(UnmanagedType.LPWStr)] public string? pPrinterName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? pServerName;
    public uint Attributes;
}

internal static class NativeMethods
{
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_SUCCESS = 0;

    public const uint PRINTER_ENUM_DEFAULT = 0x00000001;
    public const uint PRINTER_ENUM_LOCAL = 0x00000002;
    public const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;
    public const uint PRINTER_ENUM_SHARED = 0x00000020;

    public const uint PRINTER_ATTRIBUTE_DEFAULT = 0x00000004;
    public const uint PRINTER_ATTRIBUTE_SHARED = 0x00000008;
    public const uint PRINTER_ATTRIBUTE_NETWORK = 0x00000010;
    public const uint PRINTER_ATTRIBUTE_LOCAL = 0x00000040;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool OpenPrinter(string pPrinterName, out SafePrinterHandle phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int StartDocPrinter(SafePrinterHandle hPrinter, int level, [In] ref DOC_INFO_1 pDocInfo);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    public static extern bool StartPagePrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    public static extern bool WritePrinter(SafePrinterHandle hPrinter, IntPtr pBuf, int cbBuf, out int pcWritten);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    public static extern bool EndPagePrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    public static extern bool EndDocPrinter(SafePrinterHandle hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumPrinters(
        uint flags,
        [MarshalAs(UnmanagedType.LPWStr)] string? name,
        uint level,
        IntPtr pPrinterEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    public const uint JOB_CONTROL_PAUSE = 1;
    public const uint JOB_CONTROL_RESUME = 2;
    public const uint JOB_CONTROL_CANCEL = 3;
    public const uint JOB_CONTROL_RESTART = 4;
    public const uint JOB_CONTROL_DELETE = 5;

    public const uint DC_PAPERS = 2;
    public const ushort DC_PAPERSIZE = 3;
    public const ushort DC_DUPLEX = 7;
    public const ushort DC_PAPERNAMES = 16;
    public const ushort DC_COLORDEVICE = 32;

    public const uint SERVER_ACCESS_ADMINISTER = 0x00000001;
    public const uint SERVER_ACCESS_ENUMERATE = 0x00000002;
    public const uint PRINTER_ACCESS_ADMINISTER = 0x00000004;
    public const uint FORM_USER = 0x00000000;
    public const uint FORM_BUILTIN = 0x00000001;
    public const uint FORM_PRINTER = 0x00000002;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool OpenPrinterServer(
        [MarshalAs(UnmanagedType.LPWStr)] string? pPrinterName,
        out SafePrinterHandle phPrinter,
        ref PRINTER_DEFAULTS pDefault);

    [DllImport("winspool.drv", EntryPoint = "EnumFormsW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumForms(
        SafePrinterHandle hPrinter,
        uint Level,
        IntPtr pForm,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [DllImport("winspool.drv", EntryPoint = "AddFormW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool AddForm(
        SafePrinterHandle hPrinter,
        uint Level,
        ref FORM_INFO_1 pForm);

    [DllImport("winspool.drv", EntryPoint = "DeleteFormW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool DeleteForm(
        SafePrinterHandle hPrinter,
        [MarshalAs(UnmanagedType.LPWStr)] string pFormName);

    [DllImport("winspool.drv", EntryPoint = "SetJobW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetJob(SafePrinterHandle hPrinter, uint jobId, uint level, IntPtr pJob, uint command);

    [DllImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int DeviceCapabilities(
        string pDevice,
        string? pPort,
        ushort fwCapability,
        IntPtr pOutput,
        IntPtr pDevMode);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool GetDefaultPrinter(
        [Out] System.Text.StringBuilder? pszBuffer,
        ref uint pcchBuffer);
}

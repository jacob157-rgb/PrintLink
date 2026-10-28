using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PrintLink.Spooler.Native;

/// <summary>
/// SafeHandle wrapper for unmanaged Windows printer handles (winspool.drv).
/// Guarantees that printer handles are reliably released even during thread aborts or unhandled exceptions.
/// </summary>
public sealed class SafePrinterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafePrinterHandle() : base(true)
    {
    }

    public SafePrinterHandle(IntPtr preexistingHandle, bool ownsHandle) : base(ownsHandle)
    {
        SetHandle(preexistingHandle);
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.ClosePrinter(handle);
    }
}

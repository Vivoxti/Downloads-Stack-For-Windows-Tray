using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DownloadsStack.Interop;
using DownloadsStack.Localization;

namespace DownloadsStack.Services;

internal static class ShellService
{
    public static string GetDownloadsPath()
    {
        NativeMethods.SHGetKnownFolderPath(NativeMethods.DownloadsId, 0, 0, out var memory);
        try { return Marshal.PtrToStringUni(memory) ?? throw new IOException(Loc.T("Error_DownloadsMissing")); }
        finally { Marshal.FreeCoTaskMem(memory); }
    }

    public static void OpenFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(Loc.T("Error_FileMissing"), path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static uint Drag(string path, nint hwnd)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(Loc.T("Error_FileMissing"), path);
        NativeMethods.SHCreateItemFromParsingName(path, 0, NativeMethods.ShellItemId, out var item);
        System.Runtime.InteropServices.ComTypes.IDataObject? data = null;
        try
        {
            item.BindToHandler(0, NativeMethods.DataObjectHandler, NativeMethods.DataObjectId, out data);
            var result = NativeMethods.SHDoDragDrop(hwnd, data, 0, 1 | 2, out var effect);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            return effect; // DRAGDROP_S_CANCEL is a normal successful HRESULT.
        }
        finally
        {
            if (data is not null) Marshal.ReleaseComObject(data);
            Marshal.ReleaseComObject(item);
        }
    }
}

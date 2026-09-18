using System.Runtime.InteropServices;
using DownloadsStack.Interop;

namespace DownloadsStack.Services;

internal static class DisplayAnimationRate
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeMethods.Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    // DEVMODEW is 220 bytes. Only these two fields are used by this read-only query.
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    private struct DisplayMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(184)] public uint Frequency;
    }
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetDisplayMode(string device, int modeNumber, ref DisplayMode mode);

    internal static int ForMonitor(nint monitor)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" };
        var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
        return GetMonitorInfo(monitor, ref info) && GetDisplayMode(info.Device, -1, ref mode) &&
            mode.Frequency is >= 20 and <= 1000 ? (int)mode.Frequency : 60;
    }
}

using System.Runtime.InteropServices;
using DownloadsStack.Services;

namespace DownloadsStack.Tests;

/// <summary>
/// The tray icon is built out of the packed .ico by hand now that WinForms is gone, so the parsing of that
/// file and the shell's acceptance of its PNG payload are this application's problem rather than a library's.
/// </summary>
public class TrayIconTests
{
    // Outside the application nothing has touched WPF yet, and the pack scheme is registered on first use.
    static TrayIconTests() => _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

    [StructLayout(LayoutKind.Sequential)] private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool Icon;
        public uint HotspotX, HotspotY;
        public nint Mask, Color;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Bitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPerPixel;
        public nint Bits;
    }
    [DllImport("user32.dll")] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint handle, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);

    [Theory]
    [InlineData("tray.ico", 16)]
    [InlineData("tray.ico", 24)]
    [InlineData("tray.ico", 32)]
    [InlineData("tray.ico", 40)]
    [InlineData("tray-active.ico", 16)]
    [InlineData("tray-active.ico", 32)]
    [InlineData("app.ico", 48)]
    public void AssetProducesARealIconAtTheSizeTheShellAsksFor(string asset, int size)
    {
        var icon = TrayService.LoadIcon($"pack://application:,,,/DownloadsStack;component/Assets/{asset}", size);
        Assert.NotEqual(0, icon);
        try
        {
            Assert.True(GetIconInfo(icon, out var info));
            try
            {
                Assert.True(GetObject(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap) > 0);
                // The asset carries every size the shell may ask for; scaling one up would show.
                Assert.Equal(size, bitmap.Width);
                Assert.Equal(size, bitmap.Height);
                Assert.Equal(32, bitmap.BitsPerPixel); // Alpha, not a mask: a tray icon sits on the taskbar.
            }
            finally
            {
                if (info.Color != 0) DeleteObject(info.Color);
                if (info.Mask != 0) DeleteObject(info.Mask);
            }
        }
        finally { DestroyIcon(icon); }
    }

    [Fact]
    public void AMissingAssetReportsFailureRatherThanASilentlyEmptyIcon() =>
        Assert.Equal(0, TrayService.LoadIcon("pack://application:,,,/DownloadsStack;component/Assets/absent.ico", 16));
}

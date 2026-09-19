using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DownloadsStack.Interop;
using DownloadsStack.Localization;

namespace DownloadsStack.Services;

/// <summary>
/// The tray icon over Shell_NotifyIcon directly. WinForms owns exactly one control in this application,
/// and loading it for that cost System.Windows.Forms, System.Drawing and their satellites in every process
/// image — about sixteen megabytes of mapped code and its share of the startup work, for one icon.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private const int Callback = 0x0400 + 1; // WM_APP + 1, the message the shell sends back for our icon.
    private const uint Add = 0, Modify = 1, Delete = 2;
    private const uint HasMessage = 0x01, HasIcon = 0x02, HasTip = 0x04;
    private readonly MainWindow _window;
    private readonly HwndSource _source;
    private readonly nint _image, _activeImage;
    private readonly uint _taskbarCreated;
    private Timer? _retry;
    private int _attempts;
    private bool _open, _disposed;

    internal TrayService(MainWindow window)
    {
        _window = window;
        var size = Math.Max(16, NativeMethods.GetSystemMetricsForDpi(49, NativeMethods.GetDpiForSystem())); // SM_CXSMICON
        _image = LoadOrDefault("pack://application:,,,/Assets/tray.ico", size);
        _activeImage = LoadOrDefault("pack://application:,,,/Assets/tray-active.ico", size);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        // A plain top-level window, never shown. It has to be top-level rather than message-only: the shell
        // announces its own restart by broadcasting, and a message-only window is not in that broadcast.
        _source = new HwndSource(new HwndSourceParameters("Downloads Stack tray")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, without WS_VISIBLE.
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW: out of the taskbar and out of Alt+Tab.
            Width = 1, Height = 1, PositionX = 0, PositionY = 0
        });
        _source.AddHook(Message);
        // A menu put up for a tray click has to have a window of ours to take the foreground with, and
        // while the flyout is hidden this is the only one there is.
        window.TrayOwnerHandle = _source.Handle;
        Register();
        window.FlyoutOpenChanged += UpdateOpenState;
        UpdateOpenState(window.IsFlyoutOpen);
    }

    /// <summary>
    /// Adds the icon, and keeps trying for a few seconds if the shell is not listening yet: launched at
    /// sign-in the application can be up before the taskbar is, and a missed add leaves no icon at all.
    /// </summary>
    private void Register()
    {
        if (_disposed) return;
        // Adding an icon the shell still knows about fails, so drop any previous registration first. That
        // happens whenever this is a re-registration rather than the first one.
        Notify(Delete, 0);
        if (Notify(Add, HasMessage | HasIcon | HasTip)) { _retry?.Change(Timeout.Infinite, Timeout.Infinite); _attempts = 0; return; }
        if (++_attempts > 5) { LocalLog.Write("Tray icon", new InvalidOperationException("The shell did not accept the tray icon.")); return; }
        _retry ??= new Timer(_ => _window.Dispatcher.BeginInvoke(Register));
        _retry.Change(1000, Timeout.Infinite);
    }

    /// <summary>A damaged asset must still leave the user a clickable icon, not an empty slot in the tray.</summary>
    private static nint LoadOrDefault(string resource, int size) =>
        LoadIcon(resource, size) is var icon && icon != 0 ? icon : NativeMethods.LoadIcon(0, 32512); // IDI_APPLICATION

    /// <summary>The icon the shell shows, built at the size it asks for out of the multi-resolution asset.</summary>
    internal static nint LoadIcon(string resource, int size)
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri(resource))!.Stream;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var file = memory.ToArray();
            var count = file.Length >= 6 ? BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4)) : 0;
            var chosen = -1;
            var chosenSize = 0;
            for (var index = 0; index < count && 6 + index * 16 + 16 <= file.Length; ++index)
            {
                var entry = 6 + index * 16;
                var width = file[entry] == 0 ? 256 : file[entry];
                if (chosen < 0 || Closer(width, chosenSize, size)) { chosen = entry; chosenSize = width; }
            }
            if (chosen >= 0)
            {
                var length = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(chosen + 8));
                var offset = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(chosen + 12));
                if (length > 0 && offset > 0 && offset + (long)length <= file.Length)
                {
                    var pinned = GCHandle.Alloc(file, GCHandleType.Pinned);
                    try
                    {
                        var icon = NativeMethods.CreateIconFromResourceEx(pinned.AddrOfPinnedObject() + offset, (uint)length, true, 0x00030000, size, size, 0);
                        if (icon != 0) return icon;
                    }
                    finally { pinned.Free(); }
                }
            }
        }
        catch (Exception ex) { LocalLog.Write("Tray icon", ex); }
        return 0;
    }

    /// <summary>Exact size first, then the smallest that still covers it, and only then the largest below it.</summary>
    private static bool Closer(int candidate, int current, int wanted)
    {
        if (candidate == wanted) return true;
        if (current == wanted) return false;
        if (candidate >= wanted) return current < wanted || candidate < current;
        return current < wanted && candidate > current;
    }

    private void UpdateOpenState(bool open)
    {
        _open = open;
        Notify(Modify, HasIcon | HasTip);
    }

    private bool Notify(uint message, uint flags)
    {
        if (_disposed) return false;
        var text = Loc.T(message == Add ? "Tray_Tooltip" : _open ? "Tray_Close" : "Tray_Open");
        var data = new NativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            Window = _source.Handle,
            Id = 1,
            Flags = flags,
            CallbackMessage = Callback,
            Icon = _open ? _activeImage : _image,
            Tip = text.Length > 127 ? text[..127] : text, // The shell's field is fixed; a longer tip would throw.
            Info = "", InfoTitle = ""
        };
        return NativeMethods.ShellNotifyIcon(message, ref data);
    }

    private nint Message(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Explorer restarting takes every icon with it; the shell then asks everyone to register again.
        if (message != 0 && message == _taskbarCreated) { _attempts = 0; Register(); return 0; }
        if (message != Callback) return 0;
        handled = true;
        switch ((int)(lParam & 0xFFFF))
        {
            case 0x0202: _window.ToggleFromTray(); break; // WM_LBUTTONUP
            case 0x0205: _window.ShowTrayMenu(); break; // WM_RBUTTONUP
            // Anything else the shell forwards for this icon - a middle click, a balloon, the context
            // key - is still the user doing something, and a menu still standing there is in the way.
            case 0x0203: case 0x0208: case 0x020B: case 0x0206: _window.CloseTrayMenu(); break;
        }
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _window.FlyoutOpenChanged -= UpdateOpenState;
        var data = new NativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            Window = _source.Handle, Id = 1, Tip = "", Info = "", InfoTitle = ""
        };
        NativeMethods.ShellNotifyIcon(Delete, ref data);
        _disposed = true;
        _retry?.Dispose();
        _source.RemoveHook(Message);
        _source.Dispose();
        if (_image != 0) NativeMethods.DestroyIcon(_image);
        if (_activeImage != 0) NativeMethods.DestroyIcon(_activeImage);
    }
}

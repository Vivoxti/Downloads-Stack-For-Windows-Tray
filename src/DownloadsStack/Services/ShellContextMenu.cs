using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using DownloadsStack.Interop;

namespace DownloadsStack.Services;

internal sealed class ShellContextMenu : IDisposable
{
    private IContextMenu? _context;
    private IContextMenu2? _context2;
    private IContextMenu3? _context3;
    internal nint Handle { get; private set; }
    internal IContextMenu Context => _context!;

    internal ShellContextMenu(string path, bool extended = false)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(Localization.Loc.T("Error_FileMissing"), path);
        IShellContextItem? item = null;
        try
        {
            var itemId = NativeMethods.ShellItemId;
            ContextMenuNative.CreateItem(path, 0, in itemId, out item);
            var handler = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5"); // BHID_SFUIObject
            var menuId = typeof(IContextMenu).GUID;
            item.BindToHandler(0, in handler, in menuId, out _context);
            _context3 = _context as IContextMenu3;
            _context2 = _context as IContextMenu2;
            Handle = ContextMenuNative.CreatePopupMenu();
            if (Handle == 0) throw new Win32Exception();
            Marshal.ThrowExceptionForHR(_context.QueryContextMenu(Handle, 0, 1, 0x7FFF, extended ? 0x100u : 0u));
        }
        catch { Dispose(); throw; }
        finally { if (item is not null) Marshal.ReleaseComObject(item); }
    }

    internal static bool Show(string path, nint hwnd, int x, int y)
    {
        var modifiers = Keyboard.Modifiers;
        NativeMenuTheme.TryApply(hwnd); // Re-read Windows app theme before every menu, including after theme changes.
        using var menu = new ShellContextMenu(path, modifiers.HasFlag(ModifierKeys.Shift));
        var source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException(Localization.Loc.T("Error_WindowMissing"));
        source.AddHook(menu.WindowMessage);
        try
        {
            NativeMethods.SetForegroundWindow(hwnd);
            // RETURNCMD | RIGHTBUTTON: the returned value is a command ID, zero means cancellation.
            var command = ContextMenuNative.TrackPopupMenuEx(menu.Handle, 0x100 | 0x02, x, y, hwnd, 0);
            if (command == 0) return false;
            var info = new MenuCommandInfo
            {
                Size = (uint)Marshal.SizeOf<MenuCommandInfo>(), Mask = 0x4000 | 0x20000000,
                Window = hwnd, Verb = (nint)(command - 1), VerbUnicode = (nint)(command - 1), Show = 1,
                InvokePoint = new NativeMethods.Point { X = x, Y = y }
            };
            if (modifiers.HasFlag(ModifierKeys.Shift)) info.Mask |= 0x10000000;
            if (modifiers.HasFlag(ModifierKeys.Control)) info.Mask |= 0x40000000;
            menu.Context.InvokeCommand(in info);
            return true;
        }
        finally { source.RemoveHook(menu.WindowMessage); }
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Shell extensions use these messages for dynamic submenus, icons and keyboard navigation.
        if (message is not (0x0117 or 0x002B or 0x002C or 0x0120)) return 0;
        if (_context3 is not null)
        {
            if (_context3.HandleMenuMsg2((uint)message, (nuint)wParam, lParam, out var result) == 0)
            { handled = true; return result; }
        }
        if (_context2 is not null && message != 0x0120 &&
            _context2.HandleMenuMsg((uint)message, (nuint)wParam, lParam) == 0)
        {
            handled = true;
            return message is 0x002B or 0x002C ? 1 : 0;
        }
        return 0;
    }

    public void Dispose()
    {
        if (Handle != 0) { ContextMenuNative.DestroyMenu(Handle); Handle = 0; }
        _context2 = null; _context3 = null;
        if (_context is not null) { Marshal.ReleaseComObject(_context); _context = null; }
    }
}

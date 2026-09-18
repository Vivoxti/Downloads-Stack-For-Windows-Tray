using System.Runtime.InteropServices;

namespace DownloadsStack.Interop;

// Only the first IShellItem method is needed for this typed binding.
[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellContextItem
{
    void BindToHandler(nint context, in Guid handler, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IContextMenu menu);
}

[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
    void InvokeCommand(in MenuCommandInfo command);
    [PreserveSig] int GetCommandString(nuint command, uint flags, nint reserved, nint text, uint capacity);
}

[ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
    void InvokeCommand(in MenuCommandInfo command);
    [PreserveSig] int GetCommandString(nuint command, uint flags, nint reserved, nint text, uint capacity);
    [PreserveSig] int HandleMenuMsg(uint message, nuint wParam, nint lParam);
}

[ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
    void InvokeCommand(in MenuCommandInfo command);
    [PreserveSig] int GetCommandString(nuint command, uint flags, nint reserved, nint text, uint capacity);
    [PreserveSig] int HandleMenuMsg(uint message, nuint wParam, nint lParam);
    [PreserveSig] int HandleMenuMsg2(uint message, nuint wParam, nint lParam, out nint result);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MenuCommandInfo
{
    public uint Size, Mask;
    public nint Window, Verb, Parameters, Directory;
    public int Show;
    public uint HotKey;
    public nint Icon, Title, VerbUnicode, ParametersUnicode, DirectoryUnicode, TitleUnicode;
    public NativeMethods.Point InvokePoint;
}

internal static class ContextMenuNative
{
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void CreateItem(string path, nint context, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellContextItem item);
    [DllImport("user32.dll")] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll")] internal static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsMenu(nint menu);
    [DllImport("user32.dll")] internal static extern uint GetMenuItemID(nint menu, int position);
}

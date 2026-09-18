using System.Runtime.InteropServices;
using System.Text;

namespace DownloadsStack.Interop;

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, nint data, uint flags);
    void GetIDList(out nint pidl);
    void SetIDList(nint pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int capacity);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int command);
    void SetShowCmd(int command);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder location, int capacity, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string location, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(nint hwnd, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid id, uint propertyId)
{
    public Guid Id = id;
    public uint PropertyId = propertyId;
    public static readonly PropertyKey AppId = new(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant : IDisposable
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public nint Pointer;
    public static PropVariant String(string value) => new() { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(value) };
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    public void Dispose() => PropVariantClear(ref this);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(in PropertyKey key, out PropVariant value);
    void SetValue(in PropertyKey key, in PropVariant value);
    void Commit();
}

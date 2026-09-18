using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace DownloadsStack.Services;

internal static class NativeMenuTheme
{
    // UXTheme's Win32 menu opt-in is ordinal-only, also used by Microsoft's PowerToys.
    // Keep it optional: a missing export must not prevent opening a file menu.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetMode(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void RefreshTheme();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool AllowWindow(nint window, [MarshalAs(UnmanagedType.I1)] bool dark);
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")]
    private static extern nint GetOrdinal(nint module, nint ordinal);

    private sealed record Api(SetMode SetMode, AllowWindow? AllowWindow, RefreshTheme? Refresh, RefreshTheme? Flush);
    private static readonly Lazy<Api?> Functions = new(Load);

    private static Api? Load()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) ||
            !NativeLibrary.TryLoad(System.IO.Path.Combine(Environment.SystemDirectory, "uxtheme.dll"), out var module)) return null;
        T? Export<T>(int ordinal) where T : Delegate
        {
            var address = GetOrdinal(module, ordinal);
            return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        var setMode = Export<SetMode>(135);
        if (setMode is null) { NativeLibrary.Free(module); return null; }
        // Retain the module for the lifetime of these delegates.
        return new(setMode, Export<AllowWindow>(133), Export<RefreshTheme>(104), Export<RefreshTheme>(136));
    }

    internal static bool TryApply(nint window)
    {
        try
        {
            if (Functions.Value is not { } api) return false;
            var highContrast = SystemParameters.HighContrast;
            using var preferences = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var dark = !highContrast && preferences?.GetValue("AppsUseLightTheme") is int value && value == 0;
            api.Refresh?.Invoke();
            api.SetMode(highContrast ? 0 : dark ? 2 : 3); // Default / ForceDark / ForceLight, scoped to our process.
            api.AllowWindow?.Invoke(window, dark);
            api.Flush?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            LocalLog.Write("Native menu theme", ex);
            return false;
        }
    }
}

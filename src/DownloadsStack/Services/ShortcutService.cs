using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DownloadsStack.Interop;

namespace DownloadsStack.Services;

internal static class ShortcutService
{
    private static readonly Guid ShellLinkId = new("00021401-0000-0000-C000-000000000046");

    public static Task EnsureAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? link = null;
            try
            {
                var executable = Environment.ProcessPath!;
                var directory = Path.GetDirectoryName(executable)!;
                var target = Path.Combine(directory, "Downloads Stack.lnk");
                // Rewriting on every launch costs a COM save and a disk write, and where the application
                // is installed somewhere read-only it reported the same failure on every single start.
                if (AlreadyPointsTo(target, executable)) { completion.SetResult(); return; }
                link = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkId)!)!;
                var shellLink = (IShellLinkW)link;
                shellLink.SetPath(executable);
                shellLink.SetWorkingDirectory(directory);
                shellLink.SetDescription(Localization.Loc.T("Shortcut_Description"));
                // The icon comes out of the executable rather than a file beside it: the portable build is
                // one executable with nothing next to it, and the apphost carries the same image anyway.
                shellLink.SetIconLocation(executable, 0);
                shellLink.SetShowCmd(1);
                var store = (IPropertyStore)link;
                using var value = PropVariant.String(NativeMethods.AppId);
                store.SetValue(PropertyKey.AppId, value); store.Commit();
                ((IPersistFile)link).Save(target, true);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally { if (link is not null) Marshal.ReleaseComObject(link); }
        }) { IsBackground = true, Name = "Application shortcut (STA)" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    /// <summary>Reads the stored target rather than trusting timestamps, which a folder copy preserves.</summary>
    private static bool AlreadyPointsTo(string target, string executable)
    {
        if (!File.Exists(target)) return false;
        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkId)!)!;
            ((IPersistFile)link).Load(target, 0);
            var stored = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(stored, stored.Capacity, 0, 4); // SLGP_RAWPATH: never the 8.3 form.
            return string.Equals(stored.ToString(), executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { LocalLog.Write("Read shortcut", ex); return false; }
        finally { if (link is not null) Marshal.ReleaseComObject(link); }
    }
}

using System.IO;
using Microsoft.Win32;

namespace DownloadsStack.Services;

/// <summary>What Windows will do with this application at the next sign-in.</summary>
public enum AutostartState
{
    /// <summary>Nothing: the application is not registered to start.</summary>
    Off,
    /// <summary>Registered, and Windows will run it.</summary>
    On,
    /// <summary>Registered, but switched off under “Startup apps”. Only the user can undo that, there.</summary>
    BlockedByWindows,
}

/// <summary>
/// Starting with Windows through the per-user <c>Run</c> key. No administrator rights, no scheduled task
/// and no file in a startup folder: the same entry works for a copied-out portable build and for an
/// installed one, and the user can see and switch it off in Task Manager like every other startup app.
/// </summary>
public sealed class AutostartService
{
    /// <summary>
    /// The name of the value under the Run key, in the same spelling as the application identity Windows
    /// knows this program by. Nothing shows this string to the user: Task Manager lists a startup entry
    /// by the description of the executable it names.
    /// </summary>
    public const string ValueName = "DownloadsStack";
    private const string DefaultRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly string _runPath, _approvedPath;

    /// <summary>The paths are arguments only so the tests can work in a scratch key of their own.</summary>
    public AutostartService(string? runPath = null, string? approvedPath = null)
    {
        _runPath = runPath ?? DefaultRunPath;
        _approvedPath = approvedPath ?? DefaultApprovedPath;
    }

    /// <summary>Quoted: the path holds a space, and an unquoted one would be read as two arguments.</summary>
    public static string CommandFor(string executable) => "\"" + executable + "\" --autostart";

    public AutostartState Read()
    {
        if (StoredCommand() is null) return AutostartState.Off;
        return IsBlocked() ? AutostartState.BlockedByWindows : AutostartState.On;
    }

    public void Set(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runPath);
            key.SetValue(ValueName, CommandFor(Environment.ProcessPath!), RegistryValueKind.String);
            return;
        }
        using var run = Registry.CurrentUser.OpenSubKey(_runPath, writable: true);
        // The approval entry is left alone: it is the user's answer in Task Manager, not ours to erase,
        // and an entry it approves does nothing while there is no command to approve.
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Points a registered entry back at this executable once the one it names is gone. That is what a
    /// portable build being moved to another folder looks like, and an installer replacing one. An entry
    /// naming a copy that still exists is left alone: running a second copy once must not take the
    /// sign-in away from the copy the user registered.
    /// </summary>
    /// <returns>Whether the entry was rewritten.</returns>
    public bool Repair()
    {
        if (StoredCommand() is not { } command) return false;
        if (TargetOf(command) is { } target && File.Exists(target)) return false;
        Set(true);
        return true;
    }

    public Task<bool> RepairAsync() => Task.Run(Repair);

    private string? StoredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runPath);
        // RegistryValueOptions.None expands an %ENVIRONMENT% path, which is what Windows itself would run.
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>
    /// Whether the user switched the entry off under “Startup apps”. Windows leaves the command in place
    /// and records the answer here instead, so an entry that is plainly present can still never run. The
    /// low bit of the first byte is the switch; the rest is the time it was last flipped.
    /// </summary>
    private bool IsBlocked()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_approvedPath);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } approval && (approval[0] & 1) != 0;
    }

    /// <summary>The executable a stored command names, whether or not it is quoted.</summary>
    internal static string? TargetOf(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;
        if (command[0] == '"')
        {
            var closing = command.IndexOf('"', 1);
            return closing > 1 ? command[1..closing] : null;
        }
        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }
}

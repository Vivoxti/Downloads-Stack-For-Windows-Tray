using DownloadsStack.Services;
using Microsoft.Win32;

namespace DownloadsStack.Tests;

/// <summary>
/// A scratch pair of keys under the current user, so a test run can never write to the real startup
/// entry of the machine it runs on.
/// </summary>
internal sealed class TestRegistry : IDisposable
{
    private const string Root = @"Software\DownloadsStack.Tests";
    private readonly string _branch = Root + "\\" + Guid.NewGuid().ToString("N");
    public string RunPath => _branch + @"\Run";
    public string ApprovedPath => _branch + @"\StartupApproved\Run";
    public AutostartService Service() => new(RunPath, ApprovedPath);
    public string? Command()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(AutostartService.ValueName) as string;
    }
    public void Command(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        key.SetValue(AutostartService.ValueName, value, RegistryValueKind.String);
    }
    /// <summary>What Task Manager writes when the user works the switch there: the low bit is “off”.</summary>
    public void Approval(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ApprovedPath);
        key.SetValue(AutostartService.ValueName, new byte[] { (byte)(enabled ? 2 : 3), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }
    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_branch, throwOnMissingSubKey: false);
}

public class AutostartTests
{
    [Fact]
    public void RegisteringNamesThisExecutableAndSwitchingItOffLeavesNothingBehind()
    {
        using var registry = new TestRegistry();
        var autostart = registry.Service();
        Assert.Equal(AutostartState.Off, autostart.Read());
        autostart.Set(true);
        Assert.Equal(AutostartState.On, autostart.Read());
        // Quoted, or Windows would read a path with a space in it as two arguments and start nothing.
        Assert.Equal("\"" + Environment.ProcessPath + "\" --autostart", registry.Command());
        autostart.Set(false);
        Assert.Equal(AutostartState.Off, autostart.Read());
        Assert.Null(registry.Command());
        autostart.Set(false); // Switching off what is already off is not an error.
    }

    [Fact]
    public void AnEntryWindowsHasSwitchedOffReadsAsBlockedRatherThanAsOn()
    {
        using var registry = new TestRegistry();
        var autostart = registry.Service();
        autostart.Set(true);
        registry.Approval(false);
        // The command is plainly there and will never run. Reporting this as “on” would leave the user
        // with a ticked box and no application at sign-in.
        Assert.Equal(AutostartState.BlockedByWindows, autostart.Read());
        registry.Approval(true);
        Assert.Equal(AutostartState.On, autostart.Read());
        registry.Approval(false);
        autostart.Set(false);
        // The user's answer in Task Manager is left where it is; with no command it decides nothing.
        Assert.Equal(AutostartState.Off, autostart.Read());
        using var approved = Registry.CurrentUser.OpenSubKey(registry.ApprovedPath);
        Assert.NotNull(approved!.GetValue(AutostartService.ValueName));
    }

    [Fact]
    public void AMovedCopyIsPointedBackAtItselfAndOneThatStillExistsIsLeftAlone()
    {
        using var folder = new TestDirectory();
        using var registry = new TestRegistry();
        var autostart = registry.Service();
        Assert.False(autostart.Repair()); // Nothing registered: nothing to repair, and nothing to add.
        Assert.Equal(AutostartState.Off, autostart.Read());

        var missing = System.IO.Path.Combine(folder.Path, "moved away", "Downloads Stack.exe");
        registry.Command("\"" + missing + "\" --autostart");
        Assert.True(autostart.Repair());
        Assert.Equal(AutostartService.CommandFor(Environment.ProcessPath!), registry.Command());

        // A second copy started once must not take the sign-in away from the copy the user registered.
        var other = folder.File("Another copy.exe");
        registry.Command("\"" + other + "\" --autostart");
        Assert.False(autostart.Repair());
        Assert.Equal("\"" + other + "\" --autostart", registry.Command());
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Downloads Stack\\Downloads Stack.exe\" --autostart", "C:\\Program Files\\Downloads Stack\\Downloads Stack.exe")]
    [InlineData("\"C:\\apps\\Downloads Stack.exe\"", "C:\\apps\\Downloads Stack.exe")]
    [InlineData("C:\\apps\\stack.exe --autostart", "C:\\apps\\stack.exe")]
    [InlineData("  C:\\apps\\stack.exe  ", "C:\\apps\\stack.exe")]
    [InlineData("\"unterminated", null)]
    [InlineData("", null)]
    public void TheRegisteredExecutableIsReadOutOfTheCommandWhetherOrNotItIsQuoted(string command, string? executable) =>
        Assert.Equal(executable, AutostartService.TargetOf(command));
}

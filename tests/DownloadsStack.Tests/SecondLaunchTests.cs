using DownloadsStack.Services;

namespace DownloadsStack.Tests;

/// <summary>
/// What a launch that finds the application already running asks it to do. Windows starts this
/// application by itself at sign-in — through the startup entry, through the package startup task, and
/// through restoring what was running when the user signed out — and none of those is a request to put
/// the list on the screen.
/// </summary>
public class SecondLaunchTests
{
    [Fact]
    public void AStartupLaunchAsksForNothingAndAPersonsLaunchOpensTheList()
    {
        // The startup entry and the restart command line both carry this, and only they do.
        Assert.Null(SingleInstanceService.SecondLaunchCommand([AutostartService.Argument], false));
        Assert.Null(SingleInstanceService.SecondLaunchCommand(["--autostart", "--keyboard"], false));
        Assert.Equal(InstanceCommand.ShowCursor, SingleInstanceService.SecondLaunchCommand([], false));
        Assert.Equal(InstanceCommand.ShowKeyboard, SingleInstanceService.SecondLaunchCommand(["--keyboard"], false));
        // An argument this launch knows nothing about is still somebody double-clicking the application.
        Assert.Equal(InstanceCommand.ShowCursor, SingleInstanceService.SecondLaunchCommand(["--show"], false));
    }

    [Fact]
    public void ThePackageStartupTaskAsksForNothingThoughItsCommandLineIsBare()
    {
        // Exactly what the Store build's startup task runs, and exactly what the Start menu tile runs:
        // the executable and nothing after it. Only the activation tells them apart.
        Assert.Null(SingleInstanceService.SecondLaunchCommand([], true));
        Assert.Null(SingleInstanceService.SecondLaunchCommand(["--keyboard"], true));
        // A process without package identity has no startup task, and asking must not cost it an exception.
        Assert.False(AutostartService.LaunchedByStartupTask());
    }

    [Fact]
    public void TheStartupEntryCarriesTheArgumentThatKeepsItQuiet()
    {
        var command = AutostartService.CommandFor(@"C:\apps\Downloads Stack.exe");
        Assert.Equal("\"C:\\apps\\Downloads Stack.exe\" " + AutostartService.Argument, command);
        // The command is split the way Windows splits it, so the marker has to survive that round trip.
        Assert.Equal(@"C:\apps\Downloads Stack.exe", AutostartService.TargetOf(command));
        Assert.Null(SingleInstanceService.SecondLaunchCommand(
            command[(command.IndexOf("\" ", StringComparison.Ordinal) + 2)..].Split(' '), false));
    }
}

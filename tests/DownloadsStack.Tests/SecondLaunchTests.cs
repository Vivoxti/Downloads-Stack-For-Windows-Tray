using DownloadsStack.Services;

namespace DownloadsStack.Tests;

/// <summary>
/// What a launch that finds the application already running asks it to do. Windows starts this
/// application by itself at sign-in — through the startup entry, and again through restoring what was
/// running when the user signed out — and neither of those is a request to put the list on the screen.
/// </summary>
public class SecondLaunchTests
{
    [Fact]
    public void AStartupLaunchAsksForNothingAndAPersonsLaunchOpensTheList()
    {
        // The startup entry and the restart command line both carry this, and only they do.
        Assert.Null(SingleInstanceService.SecondLaunchCommand([AutostartService.Argument]));
        Assert.Null(SingleInstanceService.SecondLaunchCommand(["--autostart", "--keyboard"]));
        Assert.Equal(InstanceCommand.ShowCursor, SingleInstanceService.SecondLaunchCommand([]));
        Assert.Equal(InstanceCommand.ShowKeyboard, SingleInstanceService.SecondLaunchCommand(["--keyboard"]));
        // An argument this launch knows nothing about is still somebody double-clicking the application.
        Assert.Equal(InstanceCommand.ShowCursor, SingleInstanceService.SecondLaunchCommand(["--show"]));
    }

    [Fact]
    public void TheStartupEntryCarriesTheArgumentThatKeepsItQuiet()
    {
        var command = AutostartService.CommandFor(@"C:\apps\Downloads Stack.exe");
        Assert.Equal("\"C:\\apps\\Downloads Stack.exe\" " + AutostartService.Argument, command);
        // The command is split the way Windows splits it, so the marker has to survive that round trip.
        Assert.Equal(@"C:\apps\Downloads Stack.exe", AutostartService.TargetOf(command));
        Assert.Null(SingleInstanceService.SecondLaunchCommand(
            command[(command.IndexOf("\" ", StringComparison.Ordinal) + 2)..].Split(' ')));
    }

    [Fact]
    public void ARequestArrivingWithTheSignInWaveIsRefusedAndALaterOneIsNot()
    {
        // A package startup task has no command line to mark, so the only thing left to tell it apart by
        // is when it arrives: within moments of the launch that got here first.
        Assert.False(App.AcceptsShowRequest(TimeSpan.Zero));
        Assert.False(App.AcceptsShowRequest(App.StartupSettlingPeriod - TimeSpan.FromMilliseconds(1)));
        Assert.True(App.AcceptsShowRequest(App.StartupSettlingPeriod));
        Assert.True(App.AcceptsShowRequest(TimeSpan.FromHours(9)));
        // Long enough to cover a sign-in, short enough that a person is never caught by it.
        Assert.InRange(App.StartupSettlingPeriod, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
    }
}

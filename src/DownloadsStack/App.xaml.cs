using System.Windows;
using DownloadsStack.Interop;
using DownloadsStack.Localization;
using DownloadsStack.Services;

namespace DownloadsStack;

public partial class App : Application
{
    private SingleInstanceService? _instance;
    private TrayService? _tray;
    private readonly long _started = Environment.TickCount64;

    /// <summary>
    /// How long a launch has to follow this one by before it counts as somebody asking for the list.
    /// Sign-in starts this application more than once and seconds apart; a person opening it again is
    /// minutes or hours away. The cost of being wrong is a click that shows nothing and works on the
    /// second try, against a window that opens by itself on a desktop the user has not reached yet.
    /// </summary>
    internal static readonly TimeSpan StartupSettlingPeriod = TimeSpan.FromSeconds(30);

    /// <summary>Whether a second launch this long after ours is taken to be a person's doing.</summary>
    internal static bool AcceptsShowRequest(TimeSpan sinceStartup) => sinceStartup >= StartupSettlingPeriod;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            LocalLog.Write("UI", args.Exception);
            MessageBox.Show(args.Exception.Message, "Downloads Stack", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        try
        {
            Loc.UseSystemLanguage();
            _instance = new SingleInstanceService();
            if (!_instance.IsPrimary)
            {
                // Windows starting us again is not a request to show anything; only a person's launch is.
                // If the handshake fails there is nothing the user can do about it, and an error box on a
                // second launch is worse than doing nothing.
                if (SingleInstanceService.SecondLaunchCommand(e.Args) is { } command)
                    try { await _instance.SignalAsync(command); }
                    catch (Exception ex) { LocalLog.Write("Signal running instance", ex); }
                Shutdown();
                return;
            }
            NativeMethods.SetCurrentProcessExplicitAppUserModelID(NativeMethods.AppId);
            // Should Windows start this application again by itself — after an update, or after a Restart
            // Manager shutdown — it is to be started the way the startup entry starts it, so that such a
            // launch is recognisable as Windows' doing and stays quiet.
            NativeMethods.RegisterApplicationRestart(AutostartService.Argument, 0);
            var window = new MainWindow();
            MainWindow = window;
            _instance.Listen(command => Dispatcher.BeginInvoke(() =>
            {
                // Asked to go by an installer that is about to replace or delete this executable.
                if (command == InstanceCommand.Exit) { window.RequestExit(); return; }
                // Not every way Windows starts this application at sign-in carries an argument saying so:
                // a package startup task has no command line of its own. What they do have in common is
                // arriving within moments of the launch that got here first, and nobody double-clicks a
                // shortcut for an application that appeared in the tray a moment ago.
                if (!AcceptsShowRequest(TimeSpan.FromMilliseconds(Environment.TickCount64 - _started))) return;
                window.RestoreList(command == InstanceCommand.ShowCursor);
            }));
            // The tray window is the first thing here to get a render target, and the renderer cannot be
            // changed once one exists. Nothing above this line has drawn anything.
            await window.ApplyRenderModeAsync();
            _tray = new TrayService(window);
            await window.InitializeAsync();
            if (e.Args.Contains("--show")) window.RestoreList(true);
            try { await ShortcutService.EnsureAsync(); }
            catch (Exception ex) { LocalLog.Write("Shortcut", ex); window.ReportError(Loc.T("Error_Shortcut", ex.Message)); }
            // A portable copy that was moved, or replaced by an installed one, leaves the sign-in entry
            // naming an executable that is no longer there. Nothing is reported: the settings window
            // states what Windows will actually do, and it will state it truthfully either way.
            try { await new AutostartService().RepairAsync(); }
            catch (Exception ex) { LocalLog.Write("Autostart", ex); }
        }
        catch (Exception ex)
        {
            LocalLog.Write("Startup", ex);
            MessageBox.Show(ex.Message, "Downloads Stack", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

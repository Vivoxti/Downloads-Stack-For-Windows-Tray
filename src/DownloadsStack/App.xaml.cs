using System.Windows;
using DownloadsStack.Interop;
using DownloadsStack.Localization;
using DownloadsStack.Services;

namespace DownloadsStack;

public partial class App : Application
{
    private SingleInstanceService? _instance;
    private TrayService? _tray;
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
                // The app is already running. If the handshake fails there is nothing the user can do
                // about it, and an error box on a second launch is worse than doing nothing.
                try { await _instance.SignalAsync(e.Args.Contains("--keyboard") ? InstanceCommand.ShowKeyboard : InstanceCommand.ShowCursor); }
                catch (Exception ex) { LocalLog.Write("Signal running instance", ex); }
                Shutdown();
                return;
            }
            NativeMethods.SetCurrentProcessExplicitAppUserModelID(NativeMethods.AppId);
            var window = new MainWindow();
            MainWindow = window;
            _instance.Listen(command => Dispatcher.BeginInvoke(() =>
            {
                // Asked to go by an installer that is about to replace or delete this executable.
                if (command == InstanceCommand.Exit) window.RequestExit();
                else window.RestoreList(command == InstanceCommand.ShowCursor);
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

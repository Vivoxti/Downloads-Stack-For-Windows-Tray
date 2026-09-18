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
                try { await _instance.SignalAsync(!e.Args.Contains("--keyboard")); }
                catch (Exception ex) { LocalLog.Write("Signal running instance", ex); }
                Shutdown();
                return;
            }
            NativeMethods.SetCurrentProcessExplicitAppUserModelID(NativeMethods.AppId);
            var window = new MainWindow();
            MainWindow = window;
            _instance.Listen(cursor => Dispatcher.BeginInvoke(() => window.RestoreList(cursor)));
            _tray = new TrayService(window);
            await window.InitializeAsync();
            if (e.Args.Contains("--show")) window.RestoreList(true);
            try { await ShortcutService.EnsureAsync(); }
            catch (Exception ex) { LocalLog.Write("Shortcut", ex); window.ReportError(Loc.T("Error_Shortcut", ex.Message)); }
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

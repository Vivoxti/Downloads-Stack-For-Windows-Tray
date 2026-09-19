using System.IO;
using System.Windows;
using DownloadsStack.Services;

namespace DownloadsStack;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Nothing here needs a window, a tray icon or the settings on disk. These are what the installer
        // runs: they must never leave a second copy of the application sitting in the tray.
        if (args.Any(argument => argument is "--quit" or "--autostart-on" or "--autostart-off")) return Maintain(args);
        var check = args.Contains("--check-ui");
        var reportPath = Path.Combine(AppContext.BaseDirectory, "ui-check.log");
        try
        {
            var app = new App();
            app.InitializeComponent();
            if (check)
            {
                // Exercise published resources without running the tray or scanning user folders.
                var window = new MainWindow();
                var menuThemeAvailable = NativeMenuTheme.TryApply(new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle());
                var model = (MainViewModel)window.DataContext;
                var settings = new SettingsWindow(model);
                var icon = TrayService.LoadIcon("pack://application:,,,/Assets/tray.ico", 16);
                var activeIcon = TrayService.LoadIcon("pack://application:,,,/Assets/tray-active.ico", 16);
                var icons = icon != 0 && activeIcon != 0;
                if (icon != 0) Interop.NativeMethods.DestroyIcon(icon);
                if (activeIcon != 0) Interop.NativeMethods.DestroyIcon(activeIcon);
                settings.Close();
                model.Dispose();
                if (!icons) throw new InvalidOperationException("A tray icon resource did not produce an icon.");
                File.WriteAllText(reportPath, "OK: application resources, main window, settings and tray icon loaded. Native menu theme available: " + menuThemeAvailable);
                app.Shutdown();
                return 0;
            }
            return app.Run();
        }
        catch (Exception ex)
        {
            LocalLog.Write("Initialization", ex);
            if (check) File.WriteAllText(reportPath, ex.ToString());
            else MessageBox.Show(ex.GetBaseException().Message, "Downloads Stack", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    /// <summary>
    /// The command lines that do one thing and exit, with no window and no tray icon. <c>--quit</c> frees
    /// the executable before an installer replaces or deletes it; <c>--autostart-on</c> and
    /// <c>--autostart-off</c> set and clear the sign-in entry, which is how a script rolls the portable
    /// build out to a machine, and what the switch in the settings does when a person is there to click.
    /// </summary>
    private static int Maintain(string[] args)
    {
        var done = true;
        if (args.Contains("--quit"))
            try { done = SingleInstanceService.RequestExitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult(); }
            catch (Exception ex) { LocalLog.Write("Quit", ex); done = false; }
        if (args.Contains("--autostart-on") || args.Contains("--autostart-off"))
            try { new AutostartService().Set(args.Contains("--autostart-on")); }
            catch (Exception ex) { LocalLog.Write("Autostart", ex); done = false; }
        return done ? 0 : 1;
    }
}

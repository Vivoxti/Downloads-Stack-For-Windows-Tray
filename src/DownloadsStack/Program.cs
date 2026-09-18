using System.IO;
using System.Windows;
using DownloadsStack.Services;

namespace DownloadsStack;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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
                using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray.ico")).Stream;
                using var icon = new System.Drawing.Icon(stream);
                using var activeStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray-active.ico")).Stream;
                using var activeIcon = new System.Drawing.Icon(activeStream);
                settings.Close();
                model.Dispose();
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
}

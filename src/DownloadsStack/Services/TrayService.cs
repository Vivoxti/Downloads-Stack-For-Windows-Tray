using System.Windows;
using DownloadsStack.Localization;
using Forms = System.Windows.Forms;

namespace DownloadsStack.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _image;
    private readonly System.Drawing.Icon _activeImage;
    private readonly MainWindow _window;

    public TrayService(MainWindow window)
    {
        _window = window;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray.ico")).Stream;
        _image = new System.Drawing.Icon(stream);
        using var activeStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray-active.ico")).Stream;
        _activeImage = new System.Drawing.Icon(activeStream);
        _icon = new Forms.NotifyIcon { Icon = _image, Text = Loc.T("Tray_Tooltip"), Visible = true };
        _icon.MouseClick += (_, e) => window.Dispatcher.Invoke(() =>
        {
            if (e.Button == Forms.MouseButtons.Left) window.ToggleFromTray();
            else if (e.Button == Forms.MouseButtons.Right) window.ShowTrayMenu();
        });
        window.FlyoutOpenChanged += UpdateOpenState;
        UpdateOpenState(window.IsFlyoutOpen);
    }

    private void UpdateOpenState(bool open)
    {
        _icon.Icon = open ? _activeImage : _image;
        _icon.Text = Loc.T(open ? "Tray_Close" : "Tray_Open");
    }

    public void Dispose()
    {
        _window.FlyoutOpenChanged -= UpdateOpenState;
        _icon.Visible = false; _icon.Dispose(); _image.Dispose(); _activeImage.Dispose();
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DownloadsStack.Interop;
using DownloadsStack.Models;
using DownloadsStack.Services;
using DownloadsStack.Controls;
using DownloadsStack.Localization;

namespace DownloadsStack;

public enum ListWindowState { Visible, Hiding, Hidden, Dragging, Exiting }

public partial class MainWindow : Window
{
    private Point _press;
    private string? _dragPath;
    private bool _dragConsumed;
    private bool _ownedInteraction;
    private nint _lastMonitor;
    // Asking the display driver for its refresh rate is a real round trip, and positioning happens on every
    // snapshot. Neither the rate nor the scale of a monitor changes without a message saying so.
    private nint _displayMonitor;
    private double _displayScale = 1;
    private readonly MainViewModel _model;
    private string? _selectedPath;
    private int _menuDepth;
    private ListBoxItem? _contextRow;
    private ContextMenu? _trayMenu;
    private DispatcherTimer? _trayMenuWatch;
    private nint _trayMenuOwner, _trayMenuReturnTo;
    private SettingsWindow? _settingsWindow;
    private DownloadItem[] _visibleItems = [];
    private bool _exitAfterDrag;
    private bool _exitRequested;
    private long _hiddenAt;
    private int? _anchorX;
    private int _transitionId;
    internal int AnimationFrameRate { get; private set; } = 60;
    internal bool AnimationsEnabled { get; set; } = SystemParameters.ClientAreaAnimation;
    internal Func<nint> ForegroundWindow { get; set; } = NativeMethods.GetForegroundWindow;
    internal Action<nint> ActivateOperation { get; set; } = hwnd => NativeMethods.SetForegroundWindow(hwnd);
    /// <summary>The hidden window the tray icon posts to; the only window this application can put in front while the flyout is closed.</summary>
    internal nint TrayOwnerHandle { get; set; }
    internal Action<string, nint> DragOperation { get; set; } = (path, hwnd) => ShellService.Drag(path, hwnd);
    internal Action<string> OpenOperation { get; set; } = ShellService.OpenFile;
    internal Func<string, nint, int, int, bool> ContextMenuOperation { get; set; } = ShellContextMenu.Show;
    internal Func<SettingsWindow, bool?> SettingsDialogOperation { get; set; } = settings => settings.ShowDialog();
    private ListWindowState _listState = ListWindowState.Hidden;
    public bool IsFlyoutOpen => _listState is ListWindowState.Visible or ListWindowState.Dragging;
    internal event Action<bool>? FlyoutOpenChanged;
    public ListWindowState ListState
    {
        get => _listState;
        private set
        {
            var wasOpen = IsFlyoutOpen;
            _listState = value;
            if (wasOpen != IsFlyoutOpen) FlyoutOpenChanged?.Invoke(IsFlyoutOpen);
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        _model = new(Dispatcher);
        DataContext = _model;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            // This flyout is a per-pixel transparent HWND; DWM Acrylic is only used by settings.
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
                target.BackgroundColor = Colors.Transparent;
        };
        _model.ItemsUpdating += () =>
        {
            _selectedPath = (FileList.SelectedItem as DownloadItem)?.CanonicalPath;
        };
        _model.ItemsUpdated += () =>
        {
            PositionWindow(false);
        };
        // Moving the count slider changes how many rows fit without changing a single file.
        _model.LayoutChanged += () => PositionWindow(false);
        Deactivated += (_, _) => Dispatcher.BeginInvoke(CheckDeactivation, DispatcherPriority.Background);
        StateChanged += (_, _) =>
        {
            if (ListState == ListWindowState.Dragging) return;
            if (WindowState == WindowState.Minimized) MinimizeList();
        };
        Closing += (_, e) =>
        {
            if (ListState == ListWindowState.Dragging) { e.Cancel = true; _exitAfterDrag = true; return; }
            if (!_exitRequested) { e.Cancel = true; MinimizeList(); return; }
            ListState = ListWindowState.Exiting;
        };
        Closed += (_, _) => _model.Dispose();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && ListState != ListWindowState.Dragging) { MinimizeList(); e.Handled = true; }
            if (e.Key == Key.Enter && FileList.IsKeyboardFocusWithin && FileList.SelectedItem is DownloadItem file && ListState != ListWindowState.Dragging) { Open(file.FullPath); e.Handled = true; }
        };
    }

    /// <summary>Settles the renderer before any window here is given one. See MainViewModel.</summary>
    public Task ApplyRenderModeAsync() => _model.ApplyRenderModeAsync();

    public Task InitializeAsync()
    {
        new WindowInteropHelper(this).EnsureHandle();
        return _model.InitializeAsync();
    }

    internal static bool ApplyWindowAppearance(nint hwnd)
    {
        // WPF must not paint its opaque default HWND background over the DWM material.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;
        var corners = 2; // DWMWCP_ROUND; DWM supplies clipping and native shadow.
        NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        var dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var border = 0x003F3634;
        NativeMethods.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        var acrylic = 3; // DWMSBT_TRANSIENTWINDOW: Desktop Acrylic, Windows 11 22H2+.
        return NativeMethods.DwmSetWindowAttribute(hwnd, 38, ref acrylic, sizeof(int)) >= 0;
    }

    public void ToggleFromTray()
    {
        CloseTrayMenu(); // A left click on the icon dismisses the menu even when it toggles nothing.
        // The tray icon keeps delivering clicks inside a modal loop; the flyout must stay out of the way.
        if (ListState is ListWindowState.Dragging or ListWindowState.Exiting || _ownedInteraction) return;
        // Clicking the tray first deactivates the flyout. Do not reopen it on MouseClick.
        if (Environment.TickCount64 - _hiddenAt < 350) return;
        if (ListState == ListWindowState.Visible) { MinimizeList(); return; }
        RestoreList(true);
    }

    public void ShowTrayMenu()
    {
        CloseTrayMenu(); // A second right click replaces the menu instead of stacking another one on top.
        var menu = CreateTrayMenu();
        // Without this the flyout never learns a menu is up, and deactivation closes it underneath.
        menu.Opened += MenuOpened;
        menu.Closed += MenuClosed;
        // WPF raises Closed once the popup is really gone, so a replaced menu reports in late: clearing
        // unconditionally would drop the reference to its successor and leave that one open for good.
        menu.Closed += (_, _) => { if (ReferenceEquals(_trayMenu, menu)) { _trayMenu = null; EndTrayMenu(); } };
        _trayMenu = menu;
        // A tray click activates nothing, and a popup put up by a background process captures neither the
        // mouse nor the keyboard: every click outside it goes to whatever owns the foreground instead, so
        // the menu survives all of them and only its own items can close it. Taking the foreground first
        // is what makes Esc, a click anywhere else and switching applications dismiss it (KB135788).
        var owner = TrayOwnerHandle != 0 ? TrayOwnerHandle : new WindowInteropHelper(this).Handle;
        var previous = ForegroundWindow();
        _trayMenuOwner = owner;
        _trayMenuReturnTo = previous == owner ? 0 : previous;
        if (owner != 0) ActivateOperation(owner);
        menu.IsOpen = true;
        menu.Focus(); // Keyboard focus inside the popup is what Esc and the arrow keys need.
        WatchTrayMenu();
    }

    internal void CloseTrayMenu()
    {
        if (_trayMenu is null) { EndTrayMenu(); return; }
        var menu = _trayMenu;
        _trayMenu = null;
        menu.IsOpen = false;
        EndTrayMenu();
    }

    /// <summary>
    /// A popup only ever hears about input that is delivered to this process, and a menu put up for a tray
    /// click gets none: the click that opened it never made this application active, so WPF's own dismissal
    /// - a mouse capture over the whole desktop - is refused, and the menu sits there through everything
    /// the user does next. Asking the system directly is the only account of that input we can rely on:
    /// a button pressed anywhere outside the popup, Escape, or the foreground moving off to somebody else.
    /// </summary>
    private void WatchTrayMenu()
    {
        _trayMenuWatch?.Stop();
        _trayMenuWatch = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) =>
        {
            if (_trayMenu is not { IsOpen: true }) { CloseTrayMenu(); return; }
            var menu = MenuHandle(_trayMenu);
            var foreground = ForegroundWindow();
            var ours = foreground == 0 || foreground == _trayMenuOwner || foreground == menu
                || foreground == new WindowInteropHelper(this).Handle;
            // The popup has no window of its own for that first instant, and nothing can be outside a menu
            // that is not on screen yet.
            var outside = menu != 0 && PointerButtonDown() && WindowUnderPointer() != menu;
            if (!ours || Pressed(0x1B) || outside) CloseTrayMenu();
        }, Dispatcher);
    }

    private static bool Pressed(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>Left, right, middle and both side buttons: pressing any of them is the user aiming elsewhere.</summary>
    internal Func<bool> PointerButtonDown { get; set; } =
        () => Pressed(0x01) || Pressed(0x02) || Pressed(0x04) || Pressed(0x05) || Pressed(0x06);

    /// <summary>
    /// The window under the cursor rather than the popup's rectangle: a menu is drawn in a layered window
    /// with room for its shadow around it, and a press out in that margin is a press outside the menu.
    /// </summary>
    internal Func<nint> WindowUnderPointer { get; set; } =
        () => NativeMethods.GetCursorPos(out var cursor) ? NativeMethods.WindowFromPoint(cursor) : 0;

    private static nint MenuHandle(ContextMenu menu) => (PresentationSource.FromVisual(menu) as HwndSource)?.Handle ?? 0;

    /// <summary>Stops watching a tray menu that is gone and hands the foreground back where it came from.</summary>
    private void EndTrayMenu()
    {
        _trayMenuWatch?.Stop();
        _trayMenuWatch = null;
        var owner = _trayMenuOwner;
        var back = _trayMenuReturnTo;
        _trayMenuOwner = 0;
        _trayMenuReturnTo = 0;
        // KB135788: the window that put the menu up has to be poked once afterwards, or the first click
        // after it closes is swallowed by the menu's own dismissal instead of reaching what the user hit.
        if (owner != 0) NativeMethods.PostMessage(owner, 0x0000, 0, 0); // WM_NULL
        if (owner == 0) return;
        // Nothing of ours is on screen to hold the foreground we took, so leaving it on the hidden tray
        // window would leave the desktop with no active window at all: hand it to the flyout if that is
        // up, and otherwise back to whoever had it. A menu item that opened a window of its own has moved
        // the foreground already, and this then finds it there and leaves it alone.
        Dispatcher.BeginInvoke(() =>
        {
            if (ForegroundWindow() != owner) return;
            var target = IsFlyoutOpen ? new WindowInteropHelper(this).Handle : back;
            if (target != 0) ActivateOperation(target);
        }, DispatcherPriority.Background);
    }

    internal ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            Style = (Style)FindResource("TrayContextMenu")
        };
        var settings = new MenuItem { Header = Loc.T("Menu_Settings"), Style = (Style)FindResource("TrayMenuItem") };
        // Dismiss before acting: a modal dialog opened from inside the click left the menu painted over it.
        settings.Click += (_, _) => { CloseTrayMenu(); ShowSettings(this, new RoutedEventArgs()); };
        var exit = new MenuItem { Header = Loc.T("Menu_Exit"), Style = (Style)FindResource("TrayMenuItem") };
        // Tearing the window down inside the click of a menu that is still closing is not safe.
        exit.Click += (_, _) => { CloseTrayMenu(); Dispatcher.BeginInvoke(RequestExit, DispatcherPriority.Background); };
        menu.Items.Add(settings);
        menu.Items.Add(exit);
        return menu;
    }

    public void RequestExit()
    {
        if (ListState == ListWindowState.Dragging) { _exitAfterDrag = true; return; }
        _exitRequested = true;
        FlushIndexOnExit();
        Close();
        Application.Current.Shutdown();
    }

    public void RestoreList(bool cursor)
    {
        if (ListState is ListWindowState.Dragging or ListWindowState.Exiting || _ownedInteraction) return;
        var appearing = ListState is ListWindowState.Hidden or ListWindowState.Hiding;
        ++_transitionId; // Cancel completion of a previous close when reopened quickly.
        ListState = ListWindowState.Visible;
        FlyoutSurface.IsHitTestVisible = true;
        WindowState = WindowState.Normal;
        PositionWindow(cursor);
        Show();
        PositionWindow(false);
        if (appearing) AnimateAppearance();
        Activate();
        FileList.Focus();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        _model.Refresh();
    }

    public void ReportError(string message) => _model.ReportError(message);

    internal void FlushIndexOnExit()
    {
        try
        {
            if (!Task.Run(() => _model.FlushIndexAsync()).Wait(TimeSpan.FromSeconds(2)))
                LocalLog.Write("Exit index flush", new TimeoutException(Loc.T("Error_IndexFlush")));
        }
        catch (Exception ex) { LocalLog.Write("Exit index flush", ex); }
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WM_DPICHANGED and WM_DISPLAYCHANGE: the only two ways the cached scale and refresh rate go stale.
        if (message is 0x02E0 or 0x007E)
        {
            _displayMonitor = 0;
            CloseTrayMenu(); // Its popup is placed in the old pixels and would be left behind on the screen.

            Dispatcher.BeginInvoke(() => PositionWindow(false), DispatcherPriority.Loaded);
        }
        if (message == 0x0112 && ListState == ListWindowState.Dragging && (wParam.ToInt64() & 0xFFF0) == 0xF020) handled = true;
        if (ListState == ListWindowState.Dragging && (message == 0x0010 || (message == 0x0112 && (wParam.ToInt64() & 0xFFF0) == 0xF060)))
        { _exitAfterDrag = true; handled = true; }
        return 0;
    }

    private void CheckDeactivation()
    {
        // OLE runs its own message loop: WPF IsActive can lag behind actual foreground state.
        if (ForegroundWindow() != new WindowInteropHelper(this).Handle && !_ownedInteraction && _menuDepth == 0 && ListState == ListWindowState.Visible) MinimizeList(true);
    }

    private void MinimizeList(bool fromDeactivation = false)
    {
        if (ListState is ListWindowState.Dragging or ListWindowState.Exiting or ListWindowState.Hidden or ListWindowState.Hiding || _ownedInteraction || _menuDepth > 0) return;
        // Suppress only the tray click that caused deactivation, not the next click after a drag.
        _hiddenAt = fromDeactivation ? Environment.TickCount64 : 0;
        ResetPathTooltips(false);
        FlyoutSurface.IsHitTestVisible = false;
        var transition = ++_transitionId;
        FlyoutSurface.CacheMode = new BitmapCache();
        if (!AnimationsEnabled || !IsVisible)
        {
            ListState = ListWindowState.Hidden;
            Hide();
            FlyoutSurface.CacheMode = null;
            return;
        }
        ListState = ListWindowState.Hiding;
        var fade = Motion(0, 95);
        fade.Completed += (_, _) =>
        {
            if (transition != _transitionId || ListState != ListWindowState.Hiding) return;
            ListState = ListWindowState.Hidden;
            Hide();
            FlyoutSurface.CacheMode = null;
        };
        FlyoutSurface.BeginAnimation(OpacityProperty, fade);
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(0.98, 95));
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(0.98, 95));
        FlyoutOffset.BeginAnimation(TranslateTransform.YProperty, Motion(8, 95));
    }

    private DoubleAnimation Motion(double to, int milliseconds, double? from = null)
    {
        var motion = new DoubleAnimation
        {
            From = from, To = to, Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(motion, AnimationFrameRate);
        return motion;
    }

    private void AnimateAppearance()
    {
        if (!AnimationsEnabled)
        {
            SetAppearanceInstant();
            return;
        }
        FlyoutSurface.CacheMode = new BitmapCache();
        var transition = _transitionId;
        var settle = Motion(1, 170, 0.96);
        settle.Completed += (_, _) =>
        {
            if (transition == _transitionId && ListState == ListWindowState.Visible) FlyoutSurface.CacheMode = null;
        };
        FlyoutSurface.BeginAnimation(OpacityProperty, Motion(1, 150, IsVisible && FlyoutSurface.Opacity > 0 ? FlyoutSurface.Opacity : 0));
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(1, 170, 0.96));
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
        FlyoutOffset.BeginAnimation(TranslateTransform.YProperty, Motion(0, 170, 12));
    }

    private void SetAppearanceInstant()
    {
        FlyoutSurface.CacheMode = null;
        FlyoutSurface.BeginAnimation(OpacityProperty, null); FlyoutSurface.Opacity = 1;
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); FlyoutScale.ScaleX = 1;
        FlyoutScale.BeginAnimation(ScaleTransform.ScaleYProperty, null); FlyoutScale.ScaleY = 1;
        FlyoutOffset.BeginAnimation(TranslateTransform.YProperty, null); FlyoutOffset.Y = 0;
    }

    private void PositionWindow(bool cursor)
    {
        if (ListState != ListWindowState.Visible || _menuDepth > 0) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        nint monitor = _lastMonitor;
        if (cursor && NativeMethods.GetCursorPos(out var point)) { monitor = NativeMethods.MonitorFromPoint(point, 2); _anchorX = point.X; }
        if (monitor == 0) monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
        {
            monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
            if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) return;
        }
        _lastMonitor = monitor;
        if (_displayMonitor != monitor)
        {
            AnimationFrameRate = DisplayAnimationRate.ForMonitor(monitor);
            NativeMethods.GetDpiForMonitor(monitor, 0, out var dpi, out _);
            _displayScale = dpi > 0 ? dpi / 96d : 1;
            _displayMonitor = monitor;
        }
        var scale = _displayScale;
        Width = Math.Max(100, Math.Min(392, (info.Work.Right - info.Work.Left) / scale - 24));
        const double rowHeight = 50; // 46 DIP row + 2 DIP margin on each side.
        const double frameHeight = 20; // Transparent panel padding, no border.
        // The user's count sets the ceiling; the monitor sets the hard limit. A short screen shows fewer
        // than was asked for rather than running the list off the top of the work area.
        var screenHeight = Math.Max(frameHeight, (info.Work.Bottom - info.Work.Top) / scale - 24);
        var availableHeight = Math.Min(frameHeight + _model.MaxVisibleItems * rowHeight, screenHeight);
        var capacity = Math.Max(0, (int)Math.Floor((availableHeight - frameHeight) / rowHeight));
        // The model keeps the chosen order, leading file first. Take only what fits, then put that file at
        // the bottom, nearest the tray.
        var visibleItems = _model.Items.Take(capacity).Reverse().ToArray();
        // Re-seating the source regenerates every row: hover state, tooltips and label measurement all
        // start over. A watched folder republishes far more often than the visible list really changes,
        // and rebuilding on each of those is what made the list stutter during a download.
        var rebuild = !visibleItems.SequenceEqual(_visibleItems);
        if (rebuild)
        {
            var selection = (FileList.SelectedItem as DownloadItem)?.CanonicalPath ?? _selectedPath;
            _visibleItems = visibleItems;
            FileList.ItemsSource = visibleItems;
            FileList.UpdateLayout();
            FileList.SelectedItem = visibleItems.FirstOrDefault(i => string.Equals(i.CanonicalPath, selection, StringComparison.OrdinalIgnoreCase));
        }
        ResetPathTooltips(true);
        // Thumbnails are only ever requested here, and results are dropped while a drag owns the window,
        // so a row still on the placeholder asks again rather than keeping the generic icon.
        foreach (var item in visibleItems)
            if (rebuild || item.Icon is null || ReferenceEquals(item.Icon, _model.FallbackIcon)) _ = _model.LoadIconAsync(item);
        // The empty line needs its own room: a user who asked for one row still has to be able to read it.
        Height = visibleItems.Length == 0 ? Math.Min(96, screenHeight) : frameHeight + visibleItems.Length * rowHeight;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var gap = (int)Math.Round(12 * scale);
        var x = Math.Clamp((_anchorX ?? info.Work.Right) - width / 2, info.Work.Left + gap, Math.Max(info.Work.Left + gap, info.Work.Right - width - gap));
        var y = info.Work.Bottom - height - gap;
        NativeMethods.SetWindowPos(hwnd, 0, x, y, width, height, 0x0014);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _press = e.GetPosition(FileList);
        _dragConsumed = false;
        _dragPath = ItemAt(e.OriginalSource as DependencyObject)?.FullPath;
        // Opening is decided on release, so a drag never launches the file or selects the row.
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var pressedPath = _dragPath;
        _dragPath = null;
        e.Handled = true;
        if (ListState != ListWindowState.Visible || _dragConsumed || pressedPath is null) return;
        if (ItemAt(e.OriginalSource as DependencyObject) is DownloadItem file &&
            string.Equals(file.FullPath, pressedPath, StringComparison.OrdinalIgnoreCase)) Open(file.FullPath);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragPath is null || _dragConsumed) return;
        var point = e.GetPosition(FileList);
        if (Math.Abs(point.X - _press.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _press.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var path = _dragPath;
        e.Handled = true;
        RunFileDrag(path);
    }

    internal void RunFileDrag(string path)
    {
        if (ListState != ListWindowState.Visible) return;
        _dragConsumed = true;
        ListState = ListWindowState.Dragging;
        SetAppearanceInstant(); // Keep the source fixed during OLE drag, even when dragged immediately after opening.
        ResetPathTooltips(false);
        // Unlike WPF DoDragDrop, SHDoDragDrop does not clean up ListBox's mouse capture.
        Mouse.Capture(null);
        try
        {
            _model.SetDragging(true);
            DragOperation(path, new WindowInteropHelper(this).EnsureHandle());
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            Mouse.Capture(null);
            ListState = ListWindowState.Visible;
            _dragPath = null;
            try { _model.SetDragging(false); _model.Refresh(); }
            finally
            {
                // Both completed and cancelled native drags end the interaction. Reopen fresh from tray.
                MinimizeList();
                if (_exitAfterDrag) RequestExit();
            }
        }
    }

    private void ResetPathTooltips(bool enabled)
    {
        for (var i = 0; i < FileList.Items.Count; i++)
            if (FileList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem row)
            {
                ToolTipService.SetIsEnabled(row, false); // Close any tip and reset its hover session.
                if (enabled) ToolTipService.SetIsEnabled(row, true);
            }
    }

    private void PathToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (ListState != ListWindowState.Visible || _ownedInteraction || _menuDepth > 0 || Mouse.LeftButton == MouseButtonState.Pressed)
            e.Handled = true;
    }

    private void Open(string path)
    {
        try { OpenOperation(path); MinimizeList(); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex)
    {
        LocalLog.Write("Shell", ex);
        _model.ReportError(ex.Message);
        _ownedInteraction = true;
        try { MessageBox.Show(this, ex.Message, "Downloads Stack", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { _ownedInteraction = false; }
    }

    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); ++i)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var nested = FindVisual<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private DownloadItem? ItemAt(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is ListBoxItem row) return row.DataContext as DownloadItem;
            target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        return null;
    }
    private void OnRightPress(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void RowMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is ListBoxItem row) AnimateRowIcon(row, true);
    }

    private void RowMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ListBoxItem row && !FileRowState.GetIsContextTarget(row)) AnimateRowIcon(row, false);
    }

    private void AnimateRowIcon(ListBoxItem row, bool enlarged)
    {
        if (FindVisual<Image>(row) is not { } image || image.RenderTransform is not TransformGroup transform) return;
        if (transform.IsFrozen) { transform = transform.Clone(); image.RenderTransform = transform; }
        var scale = (ScaleTransform)transform.Children[0];
        var offset = (TranslateTransform)transform.Children[1];
        if (!AnimationsEnabled)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            scale.ScaleX = scale.ScaleY = enlarged ? 1.35 : 1;
            offset.Y = enlarged ? -2 : 0;
            return;
        }
        var motion = Motion(enlarged ? 1.35 : 1, enlarged ? 180 : 120);
        if (enlarged) motion.EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, motion);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, motion);
        offset.BeginAnimation(TranslateTransform.YProperty, Motion(enlarged ? -2 : 0, enlarged ? 150 : 120));
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ListState != ListWindowState.Visible || _menuDepth > 0) return;
        var item = ItemAt(e.OriginalSource as DependencyObject);
        if (item is null) return;
        FileList.SelectedItem = item;
        if (_contextRow is not null) FileRowState.SetIsContextTarget(_contextRow, false);
        _contextRow = FileList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
        if (_contextRow is not null) { FileRowState.SetIsContextTarget(_contextRow, true); AnimateRowIcon(_contextRow, true); }
        ++_menuDepth;
        ResetPathTooltips(false);
        Mouse.Capture(null);
        var invoked = false;
        try
        {
            var point = PointToScreen(e.GetPosition(this));
            invoked = ContextMenuOperation(item.FullPath, new WindowInteropHelper(this).EnsureHandle(), (int)point.X, (int)point.Y);
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            Mouse.Capture(null);
            MenuClosed(this, new RoutedEventArgs());
        }
        if (invoked) { _model.Refresh(); MinimizeList(); }
    }
    private void MenuOpened(object sender, RoutedEventArgs e) { ++_menuDepth; }
    private void MenuClosed(object sender, RoutedEventArgs e)
    {
        if (_contextRow is not null)
        {
            FileRowState.SetIsContextTarget(_contextRow, false);
            AnimateRowIcon(_contextRow, _contextRow.IsMouseOver);
        }
        _contextRow = null;
        _menuDepth = Math.Max(0, _menuDepth - 1);
        // Rebuild rows only after WPF has finished detaching the closing popup.
        Dispatcher.BeginInvoke(() => { CheckDeactivation(); PositionWindow(false); }, DispatcherPriority.Background);
    }
    private void ShowSettings(object sender, RoutedEventArgs e)
    {
        // Tray clicks are still dispatched inside the modal loop, so a second Settings could be opened
        // on top of the first. Bring the existing one forward instead.
        if (_settingsWindow is { } open) { open.Activate(); return; }
        // Opened from the tray the flyout stays closed: it is neither a usable owner nor worth showing.
        var overList = ListState == ListWindowState.Visible;
        _ownedInteraction = true;
        try
        {
            var settings = new SettingsWindow(_model);
            if (overList) settings.Owner = this;
            else settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _settingsWindow = settings;
            SettingsDialogOperation(settings);
        }
        finally
        {
            _settingsWindow = null;
            _ownedInteraction = false;
            if (overList) { Activate(); FileList.Focus(); PositionWindow(false); }
        }
    }
}

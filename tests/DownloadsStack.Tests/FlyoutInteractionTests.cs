using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using DownloadsStack.Controls;
using DownloadsStack.Localization;
using DownloadsStack.Models;
using DownloadsStack.Services;

namespace DownloadsStack.Tests;

[Collection("Localization")]
public class FlyoutInteractionTests
{
    [Fact]
    public async Task NativeDragEndsCleanlyAndTrayCanImmediatelyReopenAndClose()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                EnsureApplication();
                // Read before the model is built: the dialog below must leave it exactly as it is.
                var registered = new AutostartService().Read();
                window = new MainWindow { AnimationsEnabled = false };
                // Focus changes from the test host are unrelated to animation completion races.
                window.ForegroundWindow = () => new System.Windows.Interop.WindowInteropHelper(window).Handle;
                // A press that lands outside the popup is not delivered here either, whoever holds the
                // foreground: the window under the cursor is the only report of it that always arrives.
                window.ShowTrayMenu();
                Assert.Equal(1, MenuDepth(window));
                window.PointerButtonDown = () => true;
                window.WindowUnderPointer = () => 0x4242; // Pressed over somebody else's window.
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Equal(0, MenuDepth(window));
                Assert.Null(TrayMenu(window));
                window.PointerButtonDown = () => false;
                var model = (MainViewModel)window.DataContext;
                model.Items.Add(new DownloadItem
                {
                    SourceId = "test", FullPath = @"C:\test\example.txt", CanonicalPath = @"C:\test\example.txt",
                    Name = "example.txt", EffectiveDateUtc = DateTime.UtcNow
                });
                window.RestoreList(false);
                var list = (ListBox)window.FindName("FileList");
                list.UpdateLayout();
                var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                // Reopening on an unchanged list must reuse the rows: regenerating them drops hover state,
                // restarts tooltips and re-measures every label, which is what made the list stutter.
                window.RestoreList(false);
                list.UpdateLayout();
                Assert.Same(row, list.ItemContainerGenerator.ContainerFromIndex(0));
                var trayMenu = window.CreateTrayMenu();
                Assert.Equal(2, trayMenu.Items.Count);
                Assert.All(trayMenu.Items.OfType<MenuItem>(), command => Assert.IsType<Style>(command.Style));
                Assert.Equal(Loc.T("Menu_Settings"), Assert.IsType<MenuItem>(trayMenu.Items[0]).Header);
                Assert.Equal(Loc.T("Menu_Exit"), Assert.IsType<MenuItem>(trayMenu.Items[1]).Header);
                Assert.DoesNotContain(trayMenu.Items.OfType<Separator>(), _ => true);
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20), ((System.Windows.Media.SolidColorBrush)trayMenu.Background).Color);
                Assert.Equal(1000, ToolTipService.GetInitialShowDelay(row));
                Assert.Equal(0, ToolTipService.GetBetweenShowDelay(row));
                var opens = 0;
                window.OpenOperation = path => { Assert.Equal(@"C:\test\example.txt", path); opens++; };
                RaiseButton(row, UIElement.PreviewMouseLeftButtonDownEvent, MouseButton.Left);
                Assert.Equal(0, opens); // Pressing must leave time to start a drag.
                Assert.Null(list.SelectedItem);
                RaiseButton(row, UIElement.PreviewMouseLeftButtonUpEvent, MouseButton.Left);
                Assert.Equal(1, opens);
                Assert.Equal(ListWindowState.Hidden, window.ListState);
                window.RestoreList(false);
                list.UpdateLayout();
                row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                var menuCalls = 0;
                window.ContextMenuOperation = (_, _, _, _) =>
                {
                    menuCalls++;
                    Assert.True(FileRowState.GetIsContextTarget(row));
                    Assert.False(ToolTipService.GetIsEnabled(row));
                    PumpFor(TimeSpan.FromMilliseconds(250));
                    var icon = FindChild<Image>(row)!;
                    var transform = Assert.IsType<TransformGroup>(icon.RenderTransform);
                    Assert.Equal(1.35, Assert.IsType<ScaleTransform>(transform.Children[0]).ScaleX, 2);
                    return false; // Cancelling the native menu keeps the flyout open.
                };
                RaiseButton(row, UIElement.PreviewMouseRightButtonUpEvent, MouseButton.Right);
                Assert.Equal(1, opens);
                Assert.Equal(1, menuCalls);
                Assert.Equal(ListWindowState.Visible, window.ListState);
                PumpFor(TimeSpan.FromMilliseconds(200));
                Assert.False(FileRowState.GetIsContextTarget(row));
                list.UpdateLayout();
                row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                var calls = 0;
                window.DragOperation = (_, _) =>
                {
                    calls++;
                    Assert.Equal(ListWindowState.Dragging, window.ListState);
                    Assert.Null(Mouse.Captured);
                    RaiseButton(row, UIElement.PreviewMouseLeftButtonUpEvent, MouseButton.Left);
                    Assert.Equal(1, opens); // Ending/cancelling a drag must never launch the source file.
                    Assert.False(ToolTipService.GetIsEnabled(row));
                    window.ToggleFromTray(); // A tray callback inside OLE's nested loop must not corrupt state.
                    Assert.Equal(ListWindowState.Dragging, window.ListState);
                };
                // A completed drop and an Escape cancellation both return normally from the native operation.
                for (var i = 0; i < 2; i++)
                {
                    window.RunFileDrag(@"C:\test\example.txt");
                    Assert.Equal(ListWindowState.Hidden, window.ListState);
                    Assert.False(window.IsVisible);
                    Assert.Null(Mouse.Captured);
                    window.ToggleFromTray();
                    Assert.Equal(ListWindowState.Visible, window.ListState);
                    Assert.True(window.IsVisible);
                    list.UpdateLayout();
                    row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
                    Assert.True(ToolTipService.GetIsEnabled(row));
                    window.ToggleFromTray();
                    Assert.Equal(ListWindowState.Hidden, window.ListState);
                    window.RestoreList(false);
                }
                Assert.Equal(2, calls);
                window.AnimationsEnabled = true;
                Assert.InRange(window.AnimationFrameRate, 20, 1000);
                window.ToggleFromTray();
                Assert.Equal(ListWindowState.Hiding, window.ListState);
                window.ToggleFromTray(); // Reopen before the previous closing animation finishes.
                Assert.Equal(ListWindowState.Visible, window.ListState);
                PumpFor(TimeSpan.FromMilliseconds(250));
                Assert.True(window.IsVisible); // The obsolete completion must not hide the reopened flyout.
                Assert.Equal(ListWindowState.Visible, window.ListState);
                var surface = Assert.IsType<System.Windows.Controls.Border>(window.FindName("FlyoutSurface"));
                Assert.Null(surface.CacheMode);
                // A layered window passes the mouse straight through a pixel with no alpha, so the padding
                // and the gaps between rows have to carry the same 1/255 fill a row does. WPF hit testing
                // cannot tell the two apart, which is why this asserts the brush rather than a hit.
                Assert.Equal(1, ((SolidColorBrush)surface.Background).Color.A);
                window.ToggleFromTray();
                PumpFor(TimeSpan.FromMilliseconds(200));
                Assert.False(window.IsVisible);
                Assert.Equal(ListWindowState.Hidden, window.ListState);
                var settingsShown = 0;
                window.SettingsDialogOperation = settings =>
                {
                    settingsShown++;
                    Assert.Null(settings.Owner); // A hidden flyout is no owner to centre on.
                    Assert.Equal(WindowStartupLocation.CenterScreen, settings.WindowStartupLocation);
                    var hardware = Assert.IsType<CheckBox>(settings.FindName("Hardware"));
                    Assert.Equal(Loc.T("Settings_Hardware"), hardware.Content);
                    // Direct3D is off unless the user turned it on, and merely building the dialog must
                    // never write that answer back: this model is the real one, pointed at real settings.
                    Assert.False(hardware.IsChecked);
                    Assert.False(model.HardwareRendering);
                    // Same for the slider: it shows the saved percentage and building the dialog saves nothing.
                    var backdrop = Assert.IsType<Slider>(settings.FindName("Backdrop"));
                    Assert.Equal(0, backdrop.Minimum);
                    Assert.Equal(100, backdrop.Maximum);
                    // And the same for how many files the panel shows: whole files only, one to twenty.
                    var count = Assert.IsType<Slider>(settings.FindName("VisibleCount"));
                    Assert.Equal(SettingsData.MinVisibleItems, count.Minimum);
                    Assert.Equal(SettingsData.MaxVisibleItemsLimit, count.Maximum);
                    Assert.True(count.IsSnapToTickEnabled);
                    // A window that was never shown leaves its bindings unattached, so the saved percentage
                    // only reaches the slider once there is a real window carrying it.
                    settings.Show();
                    try
                    {
                        Assert.Equal(SettingsData.DefaultBackdropOpacity, model.BackdropOpacity);
                        Assert.Equal(model.BackdropOpacity, (int)backdrop.Value);
                        // The count the panel always fitted, and showing the dialog saves nothing either.
                        Assert.Equal(SettingsData.DefaultMaxVisibleItems, model.MaxVisibleItems);
                        Assert.Equal(model.MaxVisibleItems, (int)count.Value);
                        // Untouched, the plate is exactly the one every row had before it could be changed.
                        Assert.Equal(217, ((SolidColorBrush)model.BackdropBrush).Color.A);
                        // And the same for the order: every field is offered, the saved one is selected,
                        // and opening the dialog must not save a choice the user never made.
                        var sort = Assert.IsType<ComboBox>(settings.FindName("SortBox"));
                        var reverse = Assert.IsType<CheckBox>(settings.FindName("SortReverse"));
                        Assert.Equal(Enum.GetValues<SortField>(), sort.Items.Cast<SortOption>().Select(o => o.Field));
                        Assert.Equal(Loc.T("Sort_DateAdded"), sort.Items.Cast<SortOption>().First().Text);
                        Assert.Equal(SortField.DateAdded, (SortField)sort.SelectedValue!);
                        // The closed box renders through this template, not through the item containers.
                        // Left to a display path it stays null here, and the box prints the option's ToString.
                        Assert.Same(sort.ItemTemplate, sort.SelectionBoxItemTemplate);
                        Assert.NotNull(sort.SelectionBoxItemTemplate);
                        Assert.False(reverse.IsChecked);
                        Assert.Equal(SortField.DateAdded, model.SortBy);
                        Assert.False(model.SortReversed);
                        // Starting with Windows is read from the registry, not from settings.json, so the
                        // box states what the machine this runs on is actually set to — and building the
                        // dialog must not change it, whichever way that is.
                        var startup = Assert.IsType<CheckBox>(settings.FindName("RunAtLogon"));
                        Assert.Equal(Loc.T("Settings_RunAtLogon"), startup.Content);
                        Assert.Equal(model.RunAtLogon, startup.IsChecked == true);
                        Assert.Equal(registered, new AutostartService().Read());
                        // The line about Task Manager appears only for an entry Windows has switched off.
                        var blocked = Assert.IsType<TextBlock>(settings.FindName("StartupBlocked"));
                        Assert.Equal(model.AutostartBlocked ? Visibility.Visible : Visibility.Collapsed, blocked.Visibility);
                    }
                    finally { settings.Hide(); }
                    return true;
                };
                ((MenuItem)window.CreateTrayMenu().Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(1, settingsShown);
                // Settings opened from the tray must not pull the file list open with it.
                Assert.False(window.IsVisible);
                Assert.Equal(ListWindowState.Hidden, window.ListState);
                window.ToggleFromTray();
                Assert.True(window.IsVisible);
                // A tray menu has to count as an open menu, or deactivation closes the flyout underneath it.
                window.ShowTrayMenu();
                Assert.Equal(1, MenuDepth(window));
                window.ShowTrayMenu(); // A second right click replaces the menu rather than stacking another.
                PumpFor(TimeSpan.FromMilliseconds(200)); // WPF raises Closed once the popup is really gone.
                Assert.Equal(1, MenuDepth(window));
                window.CloseTrayMenu();
                PumpFor(TimeSpan.FromMilliseconds(200));
                // A count left behind would silently block the flyout from ever closing again.
                Assert.Equal(0, MenuDepth(window));
                // A tray menu goes up without this process owning the foreground, so nothing the user does
                // outside the popup is ever delivered to it. Taking the foreground is what fixes that, and
                // losing it again is the only report of a click that landed somewhere else entirely.
                var activated = new List<nint>();
                window.ActivateOperation = activated.Add;
                window.ShowTrayMenu();
                Assert.Equal(1, MenuDepth(window));
                Assert.Equal(new System.Windows.Interop.WindowInteropHelper(window).Handle, Assert.Single(activated));
                window.ForegroundWindow = () => 0x7777; // Another application now has the foreground.
                PumpFor(TimeSpan.FromMilliseconds(500));
                Assert.Equal(0, MenuDepth(window));
                Assert.Null(TrayMenu(window));
                window.ForegroundWindow = () => new System.Windows.Interop.WindowInteropHelper(window).Handle;
                // A press that lands outside the popup is not delivered here either, whoever holds the
                // foreground: the window under the cursor is the only report of it that always arrives.
                window.ShowTrayMenu();
                Assert.Equal(1, MenuDepth(window));
                window.PointerButtonDown = () => true;
                window.WindowUnderPointer = () => 0x4242; // Pressed over somebody else's window.
                PumpFor(TimeSpan.FromMilliseconds(400));
                Assert.Equal(0, MenuDepth(window));
                Assert.Null(TrayMenu(window));
                window.PointerButtonDown = () => false;
                completed.SetResult();
            }
            catch (Exception ex) { completed.SetException(ex); }
            finally
            {
                // Do not invoke RequestExit: this uninitialized model must not flush an empty user index.
                if (window is not null)
                {
                    typeof(MainWindow).GetField("_exitRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                    window.Close();
                }
                // The Application stays: WPF allows one per process, and the next test needs the theme.
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task TheChosenCountDecidesHowManyRowsTheFlyoutShowsAndHowTallItIs()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                EnsureApplication();
                window = new MainWindow { AnimationsEnabled = false };
                window.ForegroundWindow = () => new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var model = (MainViewModel)window.DataContext;
                for (var number = 1; number <= 5; number++)
                    model.Items.Add(new DownloadItem
                    {
                        SourceId = "test", FullPath = $@"C:\test\file{number}.txt", CanonicalPath = $@"C:\test\file{number}.txt",
                        Name = $"file{number}.txt", EffectiveDateUtc = DateTime.UtcNow.AddMinutes(-number)
                    });
                window.RestoreList(false);
                var list = (ListBox)window.FindName("FileList");
                // 20 DIP of padding and 50 DIP a row: a count the monitor can take is the count on screen.
                Assert.Equal(SettingsData.DefaultMaxVisibleItems, model.MaxVisibleItems);
                Assert.Equal(5, list.Items.Count); // Fewer files than rows asked for shows all of them.
                Assert.Equal(270, window.Height);
                // Moving the slider lays the flyout out again without a single file changing.
                model.PreviewMaxVisibleItems(2);
                Assert.Equal(2, list.Items.Count);
                Assert.Equal(120, window.Height);
                // The newest file stays at the bottom, nearest the tray, whatever is cut from the top.
                Assert.Equal("file1.txt", ((DownloadItem)list.Items[^1]).Name);
                model.PreviewMaxVisibleItems(SettingsData.MaxVisibleItemsLimit);
                Assert.Equal(5, list.Items.Count);
                completed.SetResult();
            }
            catch (Exception ex) { completed.SetException(ex); }
            finally
            {
                if (window is not null)
                {
                    typeof(MainWindow).GetField("_exitRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
                    window.Close();
                }
                // The Application stays: WPF allows one per process, and the next test needs the theme.
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static readonly object ApplicationGate = new();
    /// <summary>
    /// A resource-only <see cref="Application"/>, built once: pumping animation clocks must not invoke
    /// App.OnStartup, and WPF allows exactly one per process however many UI threads there are. Every test
    /// here runs on its own thread and leaves it standing, so whichever runs first is not the only one
    /// that gets a theme.
    /// </summary>
    private static void EnsureApplication()
    {
        lock (ApplicationGate)
        {
            if (Application.Current is not null) return;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/DownloadsStack;component/Theme.xaml")
            });
        }
    }

    private static object? TrayMenu(MainWindow window) =>
        typeof(MainWindow).GetField("_trayMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static int MenuDepth(MainWindow window) =>
        (int)typeof(MainWindow).GetField("_menuDepth", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RaiseButton(UIElement row, RoutedEvent routedEvent, MouseButton button) =>
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
        {
            RoutedEvent = routedEvent == UIElement.PreviewMouseLeftButtonUpEvent || routedEvent == UIElement.PreviewMouseRightButtonUpEvent
                ? Mouse.PreviewMouseUpEvent : Mouse.PreviewMouseDownEvent
        });

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
}

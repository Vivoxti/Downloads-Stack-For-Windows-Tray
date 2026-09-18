using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using DownloadsStack.Controls;
using DownloadsStack.Localization;
using DownloadsStack.Models;

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
            Application? app = null;
            MainWindow? window = null;
            try
            {
                // Use a resource-only Application: pumping animation clocks must not invoke App.OnStartup.
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/DownloadsStack;component/Theme.xaml")
                });
                window = new MainWindow { AnimationsEnabled = false };
                // Focus changes from the test host are unrelated to animation completion races.
                window.ForegroundWindow = () => new System.Windows.Interop.WindowInteropHelper(window).Handle;
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
                Assert.Null(((System.Windows.Controls.Border)window.FindName("FlyoutSurface")).CacheMode);
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
                app?.Shutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

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

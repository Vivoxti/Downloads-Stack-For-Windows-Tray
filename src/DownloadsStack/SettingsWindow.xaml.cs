using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using DownloadsStack.Localization;
using DownloadsStack.Models;
using Microsoft.Win32;

namespace DownloadsStack;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _model;
    private string? _selection;
    private bool _busy;
    public SettingsWindow(MainViewModel model)
    {
        InitializeComponent(); _model = model; DataContext = model;
        // Rounded corners and the dark non-client frame; the panel itself is painted opaque.
        SourceInitialized += (_, _) => MainWindow.ApplyWindowAppearance(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        FolderList.SelectionChanged += (_, _) =>
        {
            if (FolderList.SelectedItem is SourceStatus status) _selection = status.Source.Id;
            RemoveButton.IsEnabled = FolderList.SelectedItem is not null;
        };
        model.Sources.CollectionChanged += SourcesChanged;
        Closed += (_, _) => model.Sources.CollectionChanged -= SourcesChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    private void SourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    { if (_selection is not null) FolderList.SelectedItem = _model.Sources.FirstOrDefault(s => s.Source.Id == _selection); }
    private async Task ExecuteAsync(Func<Task<string?>> action)
    {
        if (_busy) return;
        _busy = true; Actions.IsEnabled = false; Message.Text = "";
        try { _selection = await action(); SourcesChanged(null, new(NotifyCollectionChangedAction.Reset)); }
        catch (Exception ex) { Services.LocalLog.Write("Settings", ex); Message.Text = Loc.T("Settings_SaveFailed", ex.Message); }
        finally { _busy = false; Actions.IsEnabled = true; }
    }
    private async void AddFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = Loc.T("Settings_PickFolder"), Multiselect = false };
        if (picker.ShowDialog(this) == true) await ExecuteAsync(() => _model.AddDirectoryAsync(picker.FolderName));
    }
    private async void RemoveFolder(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is not SourceStatus source) return;
        await ExecuteAsync(async () => { await _model.RemoveSourceAsync(source.Source.Id); return null; });
    }
    private async void AddDownloads(object sender, RoutedEventArgs e) => await ExecuteAsync(_model.AddDownloadsAsync);
    private void Retry(object sender, RoutedEventArgs e) => _model.Refresh();
    private void Done(object sender, RoutedEventArgs e) => Close();
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using DownloadsStack.Localization;
using DownloadsStack.Models;
using DownloadsStack.Services;

namespace DownloadsStack;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _settings = new();
    private readonly IndexStore _index = new();
    private readonly DownloadsService _downloads;
    private readonly IconService _icons = new();
    private SettingsData _configuration = new() { Sources = [] };
    private DownloadsSnapshot? _pending;
    private bool _dragging, _disposed;
    private string? _error;
    // Kept apart so a service warning that retracts itself cannot erase what startup had to report.
    private string? _startupMessage, _warning, _reported;
    public ObservableCollection<DownloadItem> Items { get; } = [];
    public ObservableCollection<SourceStatus> Sources { get; } = [];
    public string? Error { get => _error; private set { if (_error == value) return; _error = value; Changed(); Changed(nameof(HasError)); } }
    private void UpdateError() => Error = string.Join("\n", new[] { _startupMessage, _warning, _reported }.Where(m => !string.IsNullOrEmpty(m)));
    public bool HasError => !string.IsNullOrEmpty(Error);
    public int UnavailableCount => Sources.Count(s => s.IsUnavailable);
    public bool HasUnavailable => UnavailableCount > 0;
    public string UnavailableText => Loc.T("Unavailable_Count", UnavailableCount);
    public bool HasSources => Sources.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public bool CanAddDownloads => !_configuration.Sources.Any(s => s.Kind == "downloads");
    public string EmptyText => Loc.T(
        !HasSources ? "Empty_NoSources"
        : Sources.Any(s => s.State == SourceState.Probing) ? "Empty_Reading"
        : UnavailableCount == Sources.Count ? "Empty_AllUnavailable"
        : "Empty_NoFiles");
    /// <summary>The placeholder a row shows until the shell answers, so callers can tell a real icon from it.</summary>
    internal ImageSource FallbackIcon => _icons.Fallback;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ItemsUpdating;
    public event Action? ItemsUpdated;
    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher; _downloads = new(_index);
        _downloads.Updated += Post;
    }
    private void Post(DownloadsSnapshot snapshot)
    {
        // A scan finishing during shutdown must not throw out of the publish call, on a worker thread.
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        try { _dispatcher.BeginInvoke(() => Receive(snapshot)); }
        catch (InvalidOperationException) { /* The dispatcher shut down between the check and the post. */ }
    }
    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settings.LoadAsync();
            var indexMessage = await _index.LoadAsync();
            if (_disposed) return;
            _configuration = settings.Data;
            _startupMessage = string.Join("\n", new[] { settings.Message, indexMessage }.Where(m => m is not null));
            UpdateError();
            _downloads.ApplySources(_configuration.Sources);
        }
        catch (Exception ex) { LocalLog.Write("Load data", ex); _startupMessage = Loc.T("Error_Settings", ex.Message); UpdateError(); }
    }
    public void Refresh() => _downloads.Refresh();
    public void ReportError(string message) { _reported = message; UpdateError(); }
    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        if (!dragging && _pending is not null) { var pending = _pending; _pending = null; Receive(pending); }
        else if (!dragging) foreach (var item in Items.Where(i => !IconService.IsThumbnailFile(i.FullPath))) _ = LoadIconAsync(item);
    }
    private void Receive(DownloadsSnapshot snapshot)
    {
        if (_disposed) return;
        if (_dragging) { _pending = snapshot; return; }
        ItemsUpdating?.Invoke();
        var previous = Items.ToDictionary(i => i.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        var next = snapshot.Items.Select(i =>
        {
            if (previous.TryGetValue(i.CanonicalPath, out var old) && old.Name == i.Name && old.LastWriteTimeUtc == i.LastWriteTimeUtc && old.FullPath == i.FullPath && old.SourceId == i.SourceId && old.EffectiveDateUtc == i.EffectiveDateUtc)
            { old.DuplicateName = i.DuplicateName; return old; }
            i.Icon = _icons.Fallback; return i;
        }).ToArray();
        for (var position = 0; position < next.Length; ++position)
        {
            if (position < Items.Count && ReferenceEquals(Items[position], next[position])) continue;
            var existing = Items.IndexOf(next[position]);
            if (existing >= 0) Items.Move(existing, position); else Items.Insert(position, next[position]);
        }
        while (Items.Count > next.Length) Items.RemoveAt(Items.Count - 1);
        ReplaceSources(snapshot.Sources);
        // A retracted warning clears itself; one save hiccup must not stick for the rest of the session.
        _warning = snapshot.Warning; UpdateError();
        Changed(nameof(HasSources)); Changed(nameof(IsEmpty)); Changed(nameof(EmptyText)); Changed(nameof(UnavailableCount)); Changed(nameof(HasUnavailable)); Changed(nameof(UnavailableText)); Changed(nameof(CanAddDownloads));
        ItemsUpdated?.Invoke();
        foreach (var item in next.Where(i => !IconService.IsThumbnailFile(i.FullPath))) _ = LoadIconAsync(item);
    }
    /// <summary>
    /// Clearing and refilling raised a reset plus one event per folder on every snapshot, rebuilding the
    /// settings list and dropping its selection. Records compare by value, so an unchanged folder is silent.
    /// </summary>
    private void ReplaceSources(IReadOnlyList<SourceStatus> sources)
    {
        for (var position = 0; position < sources.Count; ++position)
            if (position < Sources.Count) { if (!Sources[position].Equals(sources[position])) Sources[position] = sources[position]; }
            else Sources.Add(sources[position]);
        while (Sources.Count > sources.Count) Sources.RemoveAt(Sources.Count - 1);
    }
    internal async Task LoadIconAsync(DownloadItem item)
    {
        var image = await _icons.GetAsync(item);
        if (!_disposed && !_dragging && Items.Contains(item)) item.Icon = image;
    }
    public async Task<string?> AddDirectoryAsync(string path)
    {
        var canonical = await Task.Run(() => FileRules.ResolvePath(path));
        var duplicate = await FindDuplicateAsync(canonical);
        if (duplicate is not null) return duplicate;
        var source = new FolderSource { Path = FileRules.Normalize(path) };
        await SaveConfigurationAsync(_configuration.Sources.Append(source).ToList()); return source.Id;
    }
    public async Task<string?> AddDownloadsAsync()
    {
        var existing = _configuration.Sources.FirstOrDefault(s => s.Kind == "downloads");
        if (existing is not null) return existing.Id;
        var canonical = await Task.Run(() => FileRules.ResolvePath(ShellService.GetDownloadsPath()));
        var duplicate = await FindDuplicateAsync(canonical);
        if (duplicate is not null)
        {
            await SaveConfigurationAsync(_configuration.Sources.Select(s => s.Id == duplicate ? s with { Kind = "downloads", Path = null } : s).ToList()); return duplicate;
        }
        var source = FolderSource.Downloads(); await SaveConfigurationAsync(_configuration.Sources.Append(source).ToList()); return source.Id;
    }
    private async Task<string?> FindDuplicateAsync(string canonical)
    {
        var snapshot = _configuration.Sources.ToArray();
        return await Task.Run(() =>
        {
            foreach (var source in snapshot)
            {
                try
                {
                    var path = FileRules.ResolvePath(source.Kind == "downloads" ? ShellService.GetDownloadsPath() : source.Path!);
                    if (string.Equals(path, canonical, StringComparison.OrdinalIgnoreCase)) return source.Id;
                }
                catch (Exception ex) { LocalLog.Write("Resolve source", ex); }
            }
            return null;
        });
    }
    public Task RemoveSourceAsync(string id) => SaveConfigurationAsync(_configuration.Sources.Where(s => s.Id != id).ToList());
    private async Task SaveConfigurationAsync(List<FolderSource> sources)
    {
        var next = _configuration with { Sources = sources };
        await _settings.SaveAsync(next);
        if (_disposed) return;
        _configuration = next; _downloads.ApplySources(sources); Changed(nameof(CanAddDownloads));
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public void Dispose() { _disposed = true; _downloads.Updated -= Post; _downloads.Dispose(); _icons.Dispose(); }
    public Task FlushIndexAsync() => _index.SaveAsync();
}

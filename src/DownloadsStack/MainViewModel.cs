using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Interop;
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
    /// <summary>
    /// Whether WPF renders through Direct3D. Creating the first window on the GPU costs about seventy
    /// megabytes of private memory which is never handed back, and this application spends nearly all of
    /// its life hidden in the tray; the software renderer pays for that in processor time, but only while
    /// an animation is actually running. Off by default for that reason.
    /// </summary>
    public bool HardwareRendering => _configuration.HardwareRendering;
    /// <summary>
    /// Whether Windows starts the application at sign-in. The registry entry is the only record of this:
    /// the user may switch it off in Task Manager just as well as here, and a copy in settings.json would
    /// start lying the moment they did.
    /// </summary>
    public bool RunAtLogon => _autostartState != AutostartState.Off;
    /// <summary>Registered, and switched off under “Startup apps”. Only the user can undo that, there.</summary>
    public bool AutostartBlocked => _autostartState == AutostartState.BlockedByWindows;
    private readonly AutostartService _autostart = new();
    private AutostartState _autostartState;
    /// <summary>
    /// Reads what Windows is set to do at sign-in. Synchronous by design: one registry value, read in
    /// microseconds, and every caller needs the answer before it can show anything truthful.
    /// </summary>
    public void RefreshAutostart()
    {
        var previous = _autostartState;
        try { _autostartState = _autostart.Read(); }
        catch (Exception ex) { LocalLog.Write("Autostart", ex); _autostartState = AutostartState.Off; }
        if (_autostartState == previous) return;
        Changed(nameof(RunAtLogon)); Changed(nameof(AutostartBlocked));
    }
    public async Task SetRunAtLogonAsync(bool enabled)
    {
        if (enabled == RunAtLogon) return;
        await Task.Run(() => _autostart.Set(enabled));
        if (_disposed) return;
        RefreshAutostart();
    }
    /// <summary>How opaque the plate under a file name is, in percent. See <see cref="BackdropBrush"/>.</summary>
    public int BackdropOpacity => _configuration.BackdropOpacity;
    private Brush _backdropBrush = BackdropBrushFor(SettingsData.DefaultBackdropOpacity);
    /// <summary>
    /// The fill every row paints behind its name. One frozen brush shared by all of them: the rows bind to
    /// this property, so moving the slider repaints them without rebuilding a single row.
    /// </summary>
    public Brush BackdropBrush => _backdropBrush;
    internal static Brush BackdropBrushFor(int percent)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(percent, 0, 100) * 2.55), 0x10, 0x11, 0x14));
        brush.Freeze(); return brush;
    }
    /// <summary>Shows a slider position on the flyout at once, without writing it to disk.</summary>
    public void PreviewBackdropOpacity(int percent)
    {
        _backdropBrush = BackdropBrushFor(percent); Changed(nameof(BackdropBrush));
    }
    private int _visibleLimit = SettingsData.DefaultMaxVisibleItems;
    /// <summary>
    /// How many of the newest files the flyout may show. What actually fits on the monitor is decided
    /// where the window is laid out; this is only the user's ceiling on it.
    /// </summary>
    public int MaxVisibleItems => _visibleLimit;
    /// <summary>Raised when the flyout has to be laid out again for a reason other than the file list changing.</summary>
    public event Action? LayoutChanged;
    /// <summary>Shows a slider position on the flyout at once, without writing it to disk.</summary>
    public void PreviewMaxVisibleItems(int count)
    {
        count = Math.Clamp(count, SettingsData.MinVisibleItems, SettingsData.MaxVisibleItemsLimit);
        if (count == _visibleLimit) return;
        _visibleLimit = count; Changed(nameof(MaxVisibleItems)); LayoutChanged?.Invoke();
    }
    public async Task SetMaxVisibleItemsAsync(int count)
    {
        count = Math.Clamp(count, SettingsData.MinVisibleItems, SettingsData.MaxVisibleItemsLimit);
        if (count == _configuration.MaxVisibleItems) { PreviewMaxVisibleItems(count); return; }
        var next = _configuration with { MaxVisibleItems = count };
        await _settings.SaveAsync(next);
        if (_disposed) return;
        _configuration = next;
        PreviewMaxVisibleItems(count);
    }
    private static readonly SortOption[] Choices = [.. Enum.GetValues<SortField>().Select(field => new SortOption(field))];
    /// <summary>Every field the list can be keyed on, in the order the dropdown offers them.</summary>
    public IReadOnlyList<SortOption> SortOptions => Choices;
    public SortField SortBy => _configuration.SortBy;
    public bool SortReversed => _configuration.SortReversed;
    /// <summary>
    /// Re-sorts what is already in memory and writes the choice. No folder is read again: an ordering only
    /// decides which of the files a source already knows about come first.
    /// </summary>
    public async Task SetSortAsync(SortField field, bool reversed)
    {
        if (field == _configuration.SortBy && reversed == _configuration.SortReversed) return;
        var next = _configuration with { SortBy = field, SortReversed = reversed };
        await _settings.SaveAsync(next);
        if (_disposed) return;
        _configuration = next;
        _downloads.ApplyOrder(next.Order);
        Changed(nameof(SortBy)); Changed(nameof(SortReversed));
    }
    public async Task SetBackdropOpacityAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent == _configuration.BackdropOpacity) { PreviewBackdropOpacity(percent); return; }
        var next = _configuration with { BackdropOpacity = percent };
        await _settings.SaveAsync(next);
        if (_disposed) return;
        _configuration = next;
        PreviewBackdropOpacity(percent); Changed(nameof(BackdropOpacity));
    }
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
    // Both files are read before anything asks for them, so the disk and the JSON reader work while WPF is
    // still building the window and creating its handle instead of after it.
    private readonly Task<(SettingsData Data, string? Message)> _settingsLoad;
    private readonly Task<string?> _indexLoad;
    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher; _downloads = new(_index);
        _downloads.Updated += Post;
        _settingsLoad = _settings.LoadAsync();
        _indexLoad = _index.LoadAsync();
        RefreshAutostart();
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
            var settings = await _settingsLoad;
            var indexMessage = await _indexLoad;
            if (_disposed) return;
            _configuration = settings.Data;
            PreviewBackdropOpacity(_configuration.BackdropOpacity); Changed(nameof(BackdropOpacity));
            PreviewMaxVisibleItems(_configuration.MaxVisibleItems);
            Changed(nameof(SortBy)); Changed(nameof(SortReversed));
            _startupMessage = string.Join("\n", new[] { settings.Message, indexMessage }.Where(m => m is not null));
            UpdateError();
            _downloads.ApplySources(_configuration.Sources, _configuration.Order);
        }
        catch (Exception ex) { LocalLog.Write("Load data", ex); _startupMessage = Loc.T("Error_Settings", ex.Message); UpdateError(); }
    }
    /// <summary>
    /// Must run before anything is given a render target: WPF creates the Direct3D device with the first
    /// one and keeps it for the life of the window, so switching later frees nothing. Awaits only the
    /// settings read, which the constructor already started.
    /// </summary>
    internal async Task ApplyRenderModeAsync()
    {
        try { ApplyRenderMode((await _settingsLoad).Data.HardwareRendering); }
        catch (Exception ex) { LocalLog.Write("Render mode", ex); } // InitializeAsync reports the failure itself.
    }
    internal static void ApplyRenderMode(bool hardware)
    {
        if (!hardware) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
    }
    public async Task SetHardwareRenderingAsync(bool enabled)
    {
        if (enabled == _configuration.HardwareRendering) return;
        var next = _configuration with { HardwareRendering = enabled };
        await _settings.SaveAsync(next);
        if (_disposed) return;
        _configuration = next;
        Changed(nameof(HardwareRendering));
    }
    public void Refresh() => _downloads.Refresh();
    public void ReportError(string message) { _reported = message; UpdateError(); }
    /// <summary>
    /// How many of the newest files can be on screen at once: never more than the user's chosen count, and
    /// the model keeps a hundred. Asking the shell about the rest is work the user never sees.
    /// </summary>
    private int Reachable => _visibleLimit;
    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        if (!dragging && _pending is not null) { var pending = _pending; _pending = null; Receive(pending); }
        else if (!dragging) PrefetchIcons(Items);
    }
    private void PrefetchIcons(IReadOnlyList<DownloadItem> items)
    {
        for (var position = 0; position < items.Count && position < Reachable; ++position)
            if (!IconService.IsThumbnailFile(items[position].FullPath)) _ = LoadIconAsync(items[position]);
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
        PrefetchIcons(next);
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
        _configuration = next; _downloads.ApplySources(sources, next.Order); Changed(nameof(CanAddDownloads));
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public void Dispose() { _disposed = true; _downloads.Updated -= Post; _downloads.Dispose(); _icons.Dispose(); }
    public Task FlushIndexAsync() => _index.SaveAsync();
}

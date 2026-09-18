using System.IO;
using DownloadsStack.Localization;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

public sealed record DownloadsSnapshot(IReadOnlyList<DownloadItem> Items, IReadOnlyList<SourceStatus> Sources, string? Warning);

public sealed class DownloadsService : IDisposable
{
    private sealed class SourceRuntime(FolderSource source, int generation)
    {
        public FolderSource Source = source;
        public int Generation = generation;
        public volatile bool Retired;
        public bool Running, Requested, PendingRequested, Initial = true;
        public string? Path, ErrorDetail;
        public SourceState State = SourceState.Probing;
        public FileSystemWatcher? Watcher;
        public Timer? Debounce;
        public Timer? StabilityTimer;
        public Dictionary<string, Candidate> Pending = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileStamp> Stable = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Old, string New)> Renames = [];
        public IReadOnlyList<DownloadItem> Items = [];
    }
    private sealed record Candidate(string FullPath, string Canonical, string Name, DateTime Creation, FileStamp Stamp);
    /// <summary>A file written this recently may still be growing, so its size has to be confirmed twice.</summary>
    private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceRuntime> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _ioSlots = new(4);
    private readonly CancellationTokenSource _stop = new();
    private readonly IndexStore _index;
    private int _generation;
    private bool _disposed;
    private string? _warning;
    public event Action<DownloadsSnapshot>? Updated;
    public DownloadsService(IndexStore index) => _index = index;

    public void ApplySources(IEnumerable<FolderSource> configured)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var sources = configured.ToArray();
            var ids = sources.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ++_generation;
            foreach (var state in _sources.Values.Where(s => !ids.Contains(s.Source.Id)).ToArray())
            {
                state.Retired = true;
                state.Watcher?.Dispose();
                state.Debounce?.Dispose();
                state.StabilityTimer?.Dispose();
                _sources.Remove(state.Source.Id);
            }
            foreach (var source in sources)
            {
                if (_sources.TryGetValue(source.Id, out var existing) && existing.Source != source)
                {
                    existing.Retired = true; existing.Watcher?.Dispose(); existing.Debounce?.Dispose(); existing.StabilityTimer?.Dispose();
                    _sources.Remove(source.Id);
                }
                if (!_sources.ContainsKey(source.Id)) _sources.Add(source.Id, new SourceRuntime(source, _generation));
            }
            _index.KeepSources(ids);
            foreach (var state in _sources.Values) RequestLocked(state);
            PublishLocked();
        }
        _ = PersistIndexAsync();
    }

    public void Refresh()
    {
        lock (_gate) foreach (var state in _sources.Values) RequestLocked(state);
    }

    private bool Current(SourceRuntime state, int generation) => !_disposed && !state.Retired &&
        state.Generation == generation && _sources.TryGetValue(state.Source.Id, out var current) && ReferenceEquals(state, current);

    private void RequestLocked(SourceRuntime state)
    {
        if (_disposed || state.Retired) return;
        state.Requested = true;
        if (state.Running) return;
        state.Running = true;
        _ = Task.Run(() => WorkerAsync(state));
    }

    private async Task WorkerAsync(SourceRuntime state)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                bool fullScan;
                lock (_gate)
                {
                    if (!Current(state, state.Generation) || (!state.Requested && !state.PendingRequested)) break;
                    fullScan = state.Requested;
                    state.Requested = false;
                    state.PendingRequested = false;
                }
                await _ioSlots.WaitAsync(_stop.Token);
                try { if (fullScan) await ScanAsync(state); else await CheckPendingAsync(state); }
                finally { _ioSlots.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LocalLog.Write("Worker", ex); }
        finally
        {
            lock (_gate)
            {
                state.Running = false;
                if (Current(state, state.Generation) && (state.Requested || state.PendingRequested))
                {
                    state.Running = true;
                    _ = Task.Run(() => WorkerAsync(state));
                }
            }
        }
    }

    private void WatchEvent(SourceRuntime state, string? oldPath = null, string? newPath = null, bool error = false)
    {
        lock (_gate)
        {
            // A source whose folder is not resolved yet must not be matched by a null path.
            if (!Current(state, state.Generation) || state.Path is null) return;
            foreach (var affected in _sources.Values.Where(s => s.Path is not null && string.Equals(s.Path, state.Path, StringComparison.OrdinalIgnoreCase)))
            {
                if (oldPath is not null && newPath is not null) affected.Renames.Add((oldPath, newPath));
                if (error) { affected.Watcher?.Dispose(); affected.Watcher = null; }
                affected.Debounce ??= new Timer(_ => { lock (_gate) RequestLocked(affected); });
                affected.Debounce.Change(400, Timeout.Infinite);
            }
        }
    }

    private void EnsureWatcher(SourceRuntime state, string path)
    {
        lock (_gate)
        {
            if (!Current(state, state.Generation)) return;
            if (state.Watcher is not null && string.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase)) return;
            state.Watcher?.Dispose(); state.Watcher = null; state.Path = path;
            if (_sources.Values.Any(s => !ReferenceEquals(s, state) && s.Watcher is not null && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        }
        // Construction and activation can block on a network share. Never hold the UI's gate here.
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
            InternalBufferSize = 32 * 1024
        };
        watcher.Created += (_, _) => WatchEvent(state);
        watcher.Changed += (_, _) => WatchEvent(state);
        watcher.Deleted += (_, _) => WatchEvent(state);
        watcher.Renamed += (_, e) => WatchEvent(state, e.OldFullPath, e.FullPath);
        watcher.Error += (_, _) => WatchEvent(state, error: true);
        lock (_gate)
        {
            if (!Current(state, state.Generation) || _sources.Values.Any(s => !ReferenceEquals(s, state) && s.Watcher is not null && string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase)))
            { watcher.Dispose(); return; }
            state.Watcher = watcher;
        }
        watcher.EnableRaisingEvents = true; // Before initial enumeration, so racing events request reconciliation.
    }

    /// <summary>
    /// The folder itself is already fully resolved, so an ordinary entry simply lives inside it. Only a
    /// reparse point still needs its own handle: opening one per file turned every scan into thousands of
    /// CreateFile calls against the same disk the download was being written to.
    /// </summary>
    private static string Canonicalize(string directory, FileInfo file) =>
        (file.Attributes & FileAttributes.ReparsePoint) != 0
            ? FileRules.ResolvePath(file.FullName)
            : System.IO.Path.Combine(directory, file.Name);

    /// <summary>Whether republishing would tell the UI anything it does not already show.</summary>
    private static bool SameItems(IReadOnlyList<DownloadItem> previous, IReadOnlyList<DownloadItem> next)
    {
        if (previous.Count != next.Count) return false;
        for (var i = 0; i < previous.Count; ++i)
        {
            DownloadItem before = previous[i], after = next[i];
            if (!string.Equals(before.CanonicalPath, after.CanonicalPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(before.FullPath, after.FullPath, StringComparison.Ordinal) ||
                !string.Equals(before.Name, after.Name, StringComparison.Ordinal) ||
                !string.Equals(before.SourceId, after.SourceId, StringComparison.Ordinal) ||
                before.LastWriteTimeUtc != after.LastWriteTimeUtc || before.EffectiveDateUtc != after.EffectiveDateUtc) return false;
        }
        return true;
    }

    private async Task ScanAsync(SourceRuntime state)
    {
        var generation = state.Generation;
        try
        {
            var path = FileRules.ResolvePath(state.Source.Kind == "downloads" ? ShellService.GetDownloadsPath() : state.Source.Path!);
            string? previousPath;
            lock (_gate) { if (!Current(state, generation)) return; previousPath = state.Path; } // EnsureWatcher overwrites it.
            EnsureWatcher(state, path);
            Dictionary<string, FileStamp> stable;
            Dictionary<string, string> aliases;
            (string Old, string New)[] renames;
            bool initial;
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                stable = new(state.Stable, StringComparer.OrdinalIgnoreCase);
                aliases = new(state.Aliases, StringComparer.OrdinalIgnoreCase);
                renames = state.Renames.ToArray();
                state.Renames.Clear();
                initial = state.Initial;
            }
            var candidates = new List<Candidate>();
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                _stop.Token.ThrowIfCancellationRequested();
                if (state.Retired) return;
                try
                {
                    if (!FileRules.Include(file.Name, file.Attributes)) continue;
                    var canonical = Canonicalize(path, file);
                    present.Add(canonical);
                    candidates.Add(new(file.FullName, canonical, file.Name, file.CreationTimeUtc, new(file.Length, file.LastWriteTimeUtc)));
                }
                catch (FileNotFoundException) { }
            }
            var ready = new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
            var changed = new List<Candidate>();
            foreach (var candidate in candidates)
                if (stable.TryGetValue(candidate.Canonical, out var stamp) && stamp == candidate.Stamp) ready[candidate.Canonical] = stamp;
                else changed.Add(candidate);
            // Only a recent write can still be in flight. Waiting on a folder of settled files made every
            // start and every reopen pay a second for nothing.
            var settling = changed.Any(c => DateTime.UtcNow - c.Stamp.LastWriteTimeUtc < SettleWindow);
            if (settling) await Task.Delay(1000, _stop.Token);
            foreach (var candidate in changed)
            {
                if (!settling) { ready[candidate.Canonical] = candidate.Stamp; continue; }
                try
                {
                    var file = new FileInfo(candidate.FullPath);
                    if (file.Exists && FileRules.Include(file.Name, file.Attributes) &&
                        file.Length == candidate.Stamp.Length && file.LastWriteTimeUtc == candidate.Stamp.LastWriteTimeUtc)
                        ready[candidate.Canonical] = candidate.Stamp;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                renames = renames.Concat(state.Renames).ToArray();
                state.Renames.Clear();
            }
            var renamedPaths = renames.Select(r => (Old: aliases.GetValueOrDefault(r.Old, FileRules.Normalize(r.Old)), New: FileRules.ResolvePath(r.New))).ToArray();
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                // Sampled here, not before the scan: a stability pass may have moved the list in between.
                var previousState = state.State;
                var previousDetail = state.ErrorDetail;
                var previousItems = state.Items;
                foreach (var rename in renamedPaths) _index.Rename(state.Source.Id, rename.Old, rename.New);
                if (initial) foreach (var candidate in candidates) _index.GetOrAdd(state.Source.Id, candidate.Canonical, candidate.Creation, true);
                state.Items = candidates.Where(c => ready.ContainsKey(c.Canonical)).Select(c => new DownloadItem
                {
                    SourceId = state.Source.Id, FullPath = c.FullPath, CanonicalPath = c.Canonical, Name = c.Name,
                    LastWriteTimeUtc = c.Stamp.LastWriteTimeUtc,
                    EffectiveDateUtc = _index.GetOrAdd(state.Source.Id, c.Canonical, c.Creation, initial)
                }).ToArray();
                state.Stable = ready;
                state.Pending = candidates.Where(c => !ready.ContainsKey(c.Canonical)).DistinctBy(c => c.Canonical, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(c => c.Canonical, StringComparer.OrdinalIgnoreCase);
                state.Aliases = candidates.ToDictionary(c => c.FullPath, c => c.Canonical, StringComparer.OrdinalIgnoreCase);
                state.Initial = false;
                state.State = SourceState.Ready; state.ErrorDetail = null;
                // Preserve old keys for renames arriving after the last capture; the queued scan moves them.
                var pendingRenameKeys = state.Renames.Select(r => aliases.GetValueOrDefault(r.Old, FileRules.Normalize(r.Old))).ToArray();
                // A destination renamed during stability waiting may not be in this enumeration yet.
                _index.Prune(state.Source.Id, present.Concat(pendingRenameKeys).Concat(renamedPaths.Select(r => r.New)));
                SchedulePendingLocked(state);
                // A watched folder re-scans on every write. Republishing an identical list rebuilt every row
                // of the flyout, which is what made the list stutter while a download was running.
                if (previousState != state.State || previousDetail != state.ErrorDetail ||
                    !string.Equals(previousPath, state.Path, StringComparison.OrdinalIgnoreCase) ||
                    !SameItems(previousItems, state.Items)) PublishLocked();
            }
            await PersistIndexAsync();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LocalLog.Write("Scan source", ex);
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                var failure = ex is UnauthorizedAccessException ? SourceState.Denied : SourceState.Unavailable;
                var changed = state.State != failure || state.ErrorDetail != ex.Message || state.Items.Count > 0;
                state.State = failure;
                state.ErrorDetail = ex.Message;
                state.Items = [];
                state.Stable.Clear();
                state.Pending.Clear();
                state.Watcher?.Dispose(); state.Watcher = null;
                if (changed) PublishLocked(); // Keep dates for unavailable sources.
            }
        }
    }

    private void SchedulePendingLocked(SourceRuntime state)
    {
        if (state.Pending.Count == 0) { state.StabilityTimer?.Change(Timeout.Infinite, Timeout.Infinite); return; }
        state.StabilityTimer ??= new Timer(_ =>
        {
            lock (_gate)
            {
                if (!Current(state, state.Generation)) return;
                state.PendingRequested = true;
                if (!state.Running) { state.Running = true; _ = Task.Run(() => WorkerAsync(state)); }
            }
        });
        state.StabilityTimer.Change(1000, Timeout.Infinite);
    }

    private async Task CheckPendingAsync(SourceRuntime state)
    {
        Candidate[] pending;
        lock (_gate) { if (!Current(state, state.Generation)) return; pending = state.Pending.Values.ToArray(); }
        var first = new List<Candidate>();
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in pending)
        {
            try
            {
                var file = new FileInfo(candidate.FullPath);
                if (!file.Exists || !FileRules.Include(file.Name, file.Attributes)) { removed.Add(candidate.Canonical); continue; }
                first.Add(candidate with { Stamp = new(file.Length, file.LastWriteTimeUtc) });
            }
            catch (IOException) { removed.Add(candidate.Canonical); }
            catch (UnauthorizedAccessException) { removed.Add(candidate.Canonical); }
        }
        if (first.Count > 0) await Task.Delay(1000, _stop.Token);
        var ready = new List<Candidate>();
        foreach (var candidate in first)
        {
            try
            {
                var file = new FileInfo(candidate.FullPath);
                if (!file.Exists) { removed.Add(candidate.Canonical); continue; }
                if (file.Length == candidate.Stamp.Length && file.LastWriteTimeUtc == candidate.Stamp.LastWriteTimeUtc && FileRules.Include(file.Name, file.Attributes)) ready.Add(candidate);
            }
            catch (IOException) { removed.Add(candidate.Canonical); }
            catch (UnauthorizedAccessException) { removed.Add(candidate.Canonical); }
        }
        lock (_gate)
        {
            if (!Current(state, state.Generation)) return;
            foreach (var path in removed) state.Pending.Remove(path);
            var items = state.Items.ToList();
            foreach (var candidate in ready)
            {
                state.Pending.Remove(candidate.Canonical);
                state.Stable[candidate.Canonical] = candidate.Stamp;
                items.RemoveAll(i => string.Equals(i.CanonicalPath, candidate.Canonical, StringComparison.OrdinalIgnoreCase));
                items.Add(new DownloadItem
                {
                    SourceId = state.Source.Id, FullPath = candidate.FullPath, CanonicalPath = candidate.Canonical, Name = candidate.Name,
                    LastWriteTimeUtc = candidate.Stamp.LastWriteTimeUtc,
                    EffectiveDateUtc = _index.GetOrAdd(state.Source.Id, candidate.Canonical, candidate.Creation, false)
                });
            }
            state.Items = items;
            if (removed.Count > 0) RequestLocked(state); // Reconcile deletions once, without perpetual full polling.
            SchedulePendingLocked(state);
            // A download in progress lands here once a second; only a settled or vanished file is news.
            if (ready.Count > 0 || removed.Count > 0) PublishLocked();
        }
        await PersistIndexAsync();
    }

    private async Task PersistIndexAsync()
    {
        try
        {
            await _index.SaveAsync(); // A no-op unless a date actually changed since the last write.
            lock (_gate)
            {
                if (_warning is null || _disposed) return;
                _warning = null;
                PublishLocked(); // A later success has to retract the warning the failure raised.
            }
        }
        catch (Exception ex)
        {
            LocalLog.Write("Save index", ex);
            var warning = Loc.T("Error_IndexSave", ex.Message);
            lock (_gate)
            {
                if (_disposed || _warning == warning) return;
                _warning = warning;
                PublishLocked();
            }
        }
    }

    private void PublishLocked() => Updated?.Invoke(new(FileRules.Merge(_sources.Values.SelectMany(s => s.Items)),
        _sources.Values.Select(s => new SourceStatus(s.Source, s.Path, s.State, s.ErrorDetail)).ToArray(), _warning));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            foreach (var state in _sources.Values) { state.Retired = true; state.Watcher?.Dispose(); state.Debounce?.Dispose(); state.StabilityTimer?.Dispose(); }
        }
        // A filesystem call may still be blocked. Its retired result cannot reach the UI or index.
        // _stop and _ioSlots outlive this call deliberately: disposing them throws inside those waiters.
    }
}

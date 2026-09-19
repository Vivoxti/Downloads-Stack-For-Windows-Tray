using System.IO;
using System.IO.Enumeration;
using DownloadsStack.Localization;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

public sealed record DownloadsSnapshot(IReadOnlyList<DownloadItem> Items, IReadOnlyList<SourceStatus> Sources, string? Warning);

public sealed class DownloadsService : IDisposable
{
    /// <summary>
    /// One file of a watched folder, kept from one scan to the next. Everything a scan needs about an
    /// unchanged file is already here, so re-reading a settled folder allocates nothing at all.
    /// </summary>
    private sealed class Entry
    {
        public required string Name;
        public required string FullPath;
        /// <summary>Equal to <see cref="FullPath"/> for an ordinary file; only a reparse point resolves elsewhere.</summary>
        public required string Canonical;
        public required DateTime CreationUtc;
        public DateTime LastAccessUtc;
        public FileStamp Stamp;
        /// <summary>False while the file may still be growing, which keeps it out of the published list.</summary>
        public bool Ready;
        public DateTime EffectiveDateUtc;
        /// <summary>Scan number that last saw this name in the folder; an older one means the file is gone.</summary>
        public int Seen;
        public SortKey Key => new(EffectiveDateUtc, Stamp.LastWriteTimeUtc, CreationUtc, LastAccessUtc, Name, FullPath);
    }

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
        public int Scan;
        // Keyed by file name: the enumerator hands names out as spans, so probing these costs no allocation.
        // Only the worker of this source ever touches them, which is what makes that safe outside the gate.
        public readonly Dictionary<string, Entry> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Entry> Pending = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Where a reparse point actually leads. An ordinary file is its own canonical path.</summary>
        public readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Old, string New)> Renames = [];
        public IReadOnlyList<DownloadItem> Items = [];
        /// <summary>Reused scratch space for picking the newest files; never published.</summary>
        public readonly List<Entry> Ranking = [];
    }

    /// <summary>A file written this recently may still be growing, so its size has to be confirmed twice.</summary>
    private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(5);
    /// <summary>Past this many files, one more directory enumeration beats opening a handle for each of them.</summary>
    private const int RestatLimit = 64;
    /// <summary>What <see cref="FileRules.Merge"/> keeps, so a source can stop building items past it.</summary>
    private const int ItemLimit = 100;
    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0, // FileRules decides; hidden and system files must be rejected by one rule only.
        ReturnSpecialDirectories = false
    };
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceRuntime> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _ioSlots = new(4);
    private readonly CancellationTokenSource _stop = new();
    private readonly IndexStore _index;
    private int _generation;
    private bool _disposed;
    private string? _warning;
    private FileOrder _order;
    /// <summary>Held rather than rebuilt: one delegate per ordering, not one per scan of a folder.</summary>
    private Comparison<Entry> _rank = (first, second) => FileRules.Compare(first.Key, second.Key, default);
    public event Action<DownloadsSnapshot>? Updated;
    public DownloadsService(IndexStore index) => _index = index;

    /// <summary>
    /// Changes what the list is keyed on. Nothing is read from disk again: every source already holds the
    /// dates and names an ordering compares, so this is a re-sort of what is in memory.
    /// </summary>
    public void ApplyOrder(FileOrder order)
    {
        lock (_gate)
        {
            if (_disposed || order == _order) return;
            SetOrderLocked(order);
            foreach (var state in _sources.Values) state.Items = RankLocked(state);
            PublishLocked();
        }
    }

    private void SetOrderLocked(FileOrder order)
    {
        _order = order;
        _rank = (first, second) => FileRules.Compare(first.Key, second.Key, order);
    }

    public void ApplySources(IEnumerable<FolderSource> configured, FileOrder order = default)
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Set before anything is ranked, so a first scan never has to be published twice.
            if (order != _order) SetOrderLocked(order);
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
                before.LastWriteTimeUtc != after.LastWriteTimeUtc || before.EffectiveDateUtc != after.EffectiveDateUtc ||
                before.CreationTimeUtc != after.CreationTimeUtc || before.LastAccessTimeUtc != after.LastAccessTimeUtc) return false;
        }
        return true;
    }

    /// <summary>
    /// Reconciles one directory record against what the previous scan left behind. It runs inside the
    /// enumerator, where the name is still only a span: a file that has not moved is recognised, marked as
    /// seen and dismissed without a single allocation. Only a new or altered file is returned and costs work.
    /// </summary>
    private sealed class ScanPass
    {
        public required DownloadsService Owner;
        public required SourceRuntime State;
        public required string Directory;
        public required int Number;
        public int Seen;
        /// <summary>A link that now leads elsewhere leaves its old destination behind in the index.</summary>
        public bool Retargeted;

        public Entry? Observe(ref FileSystemEntry raw)
        {
            Owner._stop.Token.ThrowIfCancellationRequested();
            if (State.Retired) throw new OperationCanceledException();
            var name = raw.FileName;
            if (!FileRules.Include(name, raw.Attributes)) return null;
            var stamp = new FileStamp(raw.Length, raw.LastWriteTimeUtc.UtcDateTime);
            ++Seen;
            if (State.Files.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var existing))
            {
                existing.Seen = Number;
                // Two field copies out of the record the enumerator already holds. Neither belongs in the
                // stamp: a file is not unfinished because Windows noted that something read it.
                existing.CreationUtc = raw.CreationTimeUtc.UtcDateTime;
                existing.LastAccessUtc = raw.LastAccessTimeUtc.UtcDateTime;
                if (existing.Ready && existing.Stamp == stamp) return null;
                existing.Stamp = stamp;
                existing.Ready = false;
                // Only a reparse point can keep its name and lead somewhere else, and only a changed one is
                // worth the handle: this is the one case where a known name has to be resolved again.
                if ((raw.Attributes & FileAttributes.ReparsePoint) != 0 &&
                    FileRules.ResolvePath(existing.FullPath) is var resolved &&
                    !string.Equals(resolved, existing.Canonical, StringComparison.OrdinalIgnoreCase))
                {
                    existing.Canonical = resolved;
                    existing.EffectiveDateUtc = default; // Dates are kept per destination, not per name.
                    State.Aliases[existing.FullPath] = resolved;
                    Retargeted = true;
                }
                return existing;
            }
            var text = name.ToString();
            var full = System.IO.Path.Join(Directory, text);
            // The folder itself is already fully resolved, so an ordinary entry simply lives inside it. Only a
            // reparse point still needs its own handle: opening one per file turned every scan into thousands of
            // CreateFile calls against the same disk the download was being written to.
            var canonical = (raw.Attributes & FileAttributes.ReparsePoint) != 0 ? FileRules.ResolvePath(full) : full;
            var entry = new Entry
            {
                Name = text, FullPath = full, Canonical = canonical, CreationUtc = raw.CreationTimeUtc.UtcDateTime,
                LastAccessUtc = raw.LastAccessTimeUtc.UtcDateTime, Stamp = stamp, Seen = Number
            };
            State.Files[text] = entry;
            if (!ReferenceEquals(canonical, full)) State.Aliases[full] = canonical;
            return entry;
        }
    }

    /// <summary>Second look at files whose size or write time moved, to tell a finished file from a growing one.</summary>
    private sealed class ConfirmPass
    {
        public required SourceRuntime State;
        public required int Number;

        public bool Check(ref FileSystemEntry raw)
        {
            var name = raw.FileName;
            if (!FileRules.Include(name, raw.Attributes)) return false;
            if (!State.Files.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var entry) ||
                entry.Ready || entry.Seen != Number) return false;
            if (entry.Stamp == new FileStamp(raw.Length, raw.LastWriteTimeUtc.UtcDateTime)) entry.Ready = true;
            return true;
        }
    }

    private static void Confirm(SourceRuntime state, string directory, List<Entry> changed, int number)
    {
        if (changed.Count > RestatLimit)
        {
            // Confirming a whole folder one handle at a time is thousands of CreateFile calls. One more
            // directory enumeration answers for all of them at once, which is what a first scan needs.
            var pass = new ConfirmPass { State = state, Number = number };
            foreach (var _ in new FileSystemEnumerable<bool>(directory, pass.Check, ScanOptions)) { }
            return;
        }
        foreach (var entry in changed)
        {
            try
            {
                var file = new FileInfo(entry.FullPath);
                if (file.Exists && FileRules.Include(entry.Name, file.Attributes) &&
                    file.Length == entry.Stamp.Length && file.LastWriteTimeUtc == entry.Stamp.LastWriteTimeUtc)
                    entry.Ready = true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
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
            (string Old, string New)[] renames;
            bool initial;
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                renames = state.Renames.ToArray();
                state.Renames.Clear();
                initial = state.Initial;
            }
            var number = ++state.Scan;
            var pass = new ScanPass { Owner = this, State = state, Directory = path, Number = number };
            var changed = new List<Entry>();
            foreach (var entry in new FileSystemEnumerable<Entry?>(path, pass.Observe, ScanOptions))
                if (entry is not null) changed.Add(entry);
            // Only a recent write can still be in flight. Waiting on a folder of settled files made every
            // start and every reopen pay a second for nothing.
            var settling = false;
            foreach (var entry in changed)
                if (DateTime.UtcNow - entry.Stamp.LastWriteTimeUtc < SettleWindow) { settling = true; break; }
            if (settling)
            {
                await Task.Delay(1000, _stop.Token);
                if (state.Retired) return;
                Confirm(state, path, changed, number);
            }
            else foreach (var entry in changed) entry.Ready = true;
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                renames = [.. renames, .. state.Renames];
                state.Renames.Clear();
            }
            // Translated before the sweep below, while a reparse point that has just left still has its alias.
            var renamed = renames.Select(r => (Old: state.Aliases.GetValueOrDefault(r.Old, FileRules.Normalize(r.Old)), New: FileRules.ResolvePath(r.New))).ToArray();
            lock (_gate)
            {
                if (!Current(state, generation)) return;
                // Sampled here, not before the scan: a stability pass may have moved the list in between.
                var previousState = state.State;
                var previousDetail = state.ErrorDetail;
                var previousItems = state.Items;
                foreach (var rename in renamed) _index.Rename(state.Source.Id, rename.Old, rename.New);
                // An equal count means every remembered file answered this enumeration, so nothing was deleted.
                List<string>? gone = null;
                if (pass.Seen != state.Files.Count)
                    foreach (var (name, entry) in state.Files)
                        if (entry.Seen != number) (gone ??= []).Add(name);
                if (gone is not null)
                    foreach (var name in gone)
                        if (state.Files.Remove(name, out var removed))
                        { state.Pending.Remove(name); state.Aliases.Remove(removed.FullPath); }
                // Everything in this list answered the enumeration, so the sweep above kept all of it.
                foreach (var entry in changed)
                    if (entry.Ready) state.Pending.Remove(entry.Name);
                    else state.Pending[entry.Name] = entry;
                if (initial)
                    foreach (var entry in state.Files.Values)
                        entry.EffectiveDateUtc = _index.GetOrAddNormalized(state.Source.Id, entry.Canonical, entry.CreationUtc, true);
                else
                    foreach (var entry in changed)
                        if (entry.Ready && entry.EffectiveDateUtc == default)
                            entry.EffectiveDateUtc = _index.GetOrAddNormalized(state.Source.Id, entry.Canonical, entry.CreationUtc, false);
                state.Initial = false;
                state.State = SourceState.Ready; state.ErrorDetail = null;
                if (gone is not null || initial || pass.Retargeted) PruneLocked(state, renamed);
                var moved = changed.Count > 0 || gone is not null || renamed.Length > 0 || initial;
                if (moved) state.Items = RankLocked(state);
                SchedulePendingLocked(state);
                // A watched folder re-scans on every write. Republishing an identical list rebuilt every row
                // of the flyout, which is what made the list stutter while a download was running.
                if (previousState != state.State || previousDetail != state.ErrorDetail ||
                    !string.Equals(previousPath, state.Path, StringComparison.OrdinalIgnoreCase) ||
                    (moved && !SameItems(previousItems, state.Items))) PublishLocked();
            }
            // Cheap when nothing changed, and the retry that clears a warning left by an earlier failed write.
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
                state.Files.Clear();
                state.Pending.Clear();
                state.Aliases.Clear();
                state.Watcher?.Dispose(); state.Watcher = null;
                if (changed) PublishLocked(); // Keep dates for unavailable sources.
            }
        }
    }

    /// <summary>The leading files of one source, already cut to the global limit so the rest never becomes items.</summary>
    private DownloadItem[] RankLocked(SourceRuntime state)
    {
        state.Ranking.Clear();
        foreach (var entry in state.Files.Values) if (entry.Ready) state.Ranking.Add(entry);
        state.Ranking.Sort(_rank);
        var items = new DownloadItem[Math.Min(state.Ranking.Count, ItemLimit)];
        for (var position = 0; position < items.Length; ++position)
        {
            var entry = state.Ranking[position];
            items[position] = new DownloadItem
            {
                SourceId = state.Source.Id, FullPath = entry.FullPath, CanonicalPath = entry.Canonical, Name = entry.Name,
                LastWriteTimeUtc = entry.Stamp.LastWriteTimeUtc, EffectiveDateUtc = entry.EffectiveDateUtc,
                CreationTimeUtc = entry.CreationUtc, LastAccessTimeUtc = entry.LastAccessUtc
            };
        }
        state.Ranking.Clear(); // Never hold on to entries that a later sweep may drop.
        return items;
    }

    private void PruneLocked(SourceRuntime state, (string Old, string New)[] renamed)
    {
        var present = new HashSet<string>(state.Files.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in state.Files.Values) present.Add(entry.Canonical);
        // Preserve old keys for renames arriving after the last capture; the queued scan moves them.
        foreach (var (old, _) in state.Renames) present.Add(state.Aliases.GetValueOrDefault(old, FileRules.Normalize(old)));
        // A destination renamed during stability waiting may not be in this enumeration yet.
        foreach (var (_, destination) in renamed) present.Add(destination);
        _index.PruneNormalized(state.Source.Id, present);
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
        Entry[] pending;
        lock (_gate) { if (!Current(state, state.Generation)) return; pending = state.Pending.Values.ToArray(); }
        var first = new List<(Entry Entry, FileStamp Stamp)>();
        var removed = new List<Entry>();
        foreach (var entry in pending)
        {
            try
            {
                var file = new FileInfo(entry.FullPath);
                if (!file.Exists || !FileRules.Include(entry.Name, file.Attributes)) { removed.Add(entry); continue; }
                first.Add((entry, new(file.Length, file.LastWriteTimeUtc)));
            }
            catch (IOException) { removed.Add(entry); }
            catch (UnauthorizedAccessException) { removed.Add(entry); }
        }
        if (first.Count > 0) await Task.Delay(1000, _stop.Token);
        var ready = new List<(Entry Entry, FileStamp Stamp)>();
        foreach (var (entry, stamp) in first)
        {
            try
            {
                var file = new FileInfo(entry.FullPath);
                if (!file.Exists) { removed.Add(entry); continue; }
                if (file.Length == stamp.Length && file.LastWriteTimeUtc == stamp.LastWriteTimeUtc && FileRules.Include(entry.Name, file.Attributes)) ready.Add((entry, stamp));
            }
            catch (IOException) { removed.Add(entry); }
            catch (UnauthorizedAccessException) { removed.Add(entry); }
        }
        lock (_gate)
        {
            if (!Current(state, state.Generation)) return;
            foreach (var entry in removed) state.Pending.Remove(entry.Name);
            foreach (var (entry, stamp) in ready)
            {
                state.Pending.Remove(entry.Name);
                entry.Stamp = stamp;
                entry.Ready = true;
                if (entry.EffectiveDateUtc == default)
                    entry.EffectiveDateUtc = _index.GetOrAddNormalized(state.Source.Id, entry.Canonical, entry.CreationUtc, false);
            }
            if (ready.Count > 0) state.Items = RankLocked(state);
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

    private void PublishLocked() => Updated?.Invoke(new(FileRules.Merge(_sources.Values.SelectMany(s => s.Items), _order),
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

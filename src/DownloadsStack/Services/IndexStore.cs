using System.IO;
using System.Text.Json;
using DownloadsStack.Localization;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

public sealed class IndexStore
{
    private sealed record IndexData(int SchemaVersion, List<IndexEntry> Entries);
    private readonly string _path;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeGate = new(1);
    // Nested by source rather than one map under a composed "source\0path" key: a scan asks for every file
    // it sees, and building that key allocated two strings per file per pass.
    private readonly Dictionary<string, Dictionary<string, DateTime>> _sources = new(StringComparer.OrdinalIgnoreCase);
    // A scan runs whenever a watched folder changes; without this, every one of them rewrote the whole file.
    private long _revision, _savedRevision;
    public IndexStore(string? directory = null) => _path = Path.Combine(directory ?? LocalLog.DataDirectory, "index.json");
    /// <summary>Changes since startup. Equal values mean the in-memory index and the file agree.</summary>
    internal long Revision { get { lock (_gate) return _revision; } }

    private Dictionary<string, DateTime> MapFor(string source)
    {
        if (!_sources.TryGetValue(source, out var map)) _sources[source] = map = new(StringComparer.OrdinalIgnoreCase);
        return map;
    }

    public async Task<string?> LoadAsync() => await Task.Run(() =>
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<IndexData>(File.ReadAllText(_path), AtomicJson.Options);
            if (data is null || data.SchemaVersion != 1 || data.Entries is null) throw new JsonException(Loc.T("Error_IndexShape"));
            var validated = new List<IndexEntry>(data.Entries.Count);
            foreach (var entry in data.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.SourceId) || string.IsNullOrWhiteSpace(entry.Path) ||
                    !Path.IsPathFullyQualified(entry.Path) || entry.EffectiveDateUtc.Kind != DateTimeKind.Utc)
                    throw new JsonException(Loc.T("Error_IndexEntry"));
                validated.Add(entry);
            }
            // Loading is not a change: the file already holds exactly this.
            lock (_gate)
                foreach (var entry in validated) MapFor(entry.SourceId)[FileRules.Normalize(entry.Path)] = entry.EffectiveDateUtc;
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            LocalLog.Write("Index recovery", ex);
            // Mark dirty so the damaged file is replaced, instead of being backed up again on every start.
            lock (_gate) { _sources.Clear(); ++_revision; }
            try { AtomicJson.Backup(_path); } catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException) { LocalLog.Write("Index backup", backupError); }
            return Loc.T("Error_IndexCorrupt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write("Read index", ex);
            return Loc.T("Error_IndexRead", ex.Message);
        }
    });

    public DateTime GetOrAdd(string source, string path, DateTime creationUtc, bool initialScan) =>
        GetOrAddNormalized(source, FileRules.Normalize(path), creationUtc, initialScan);

    /// <summary>For callers whose path already came out of <see cref="FileRules.Normalize"/> — every scan result does.</summary>
    internal DateTime GetOrAddNormalized(string source, string path, DateTime creationUtc, bool initialScan)
    {
        lock (_gate)
        {
            var map = MapFor(source);
            if (map.TryGetValue(path, out var existing)) return existing;
            var date = initialScan ? creationUtc : DateTime.UtcNow;
            map[path] = date;
            ++_revision;
            return date;
        }
    }

    public void Rename(string source, string oldPath, string newPath)
    {
        lock (_gate)
        {
            var map = MapFor(source);
            if (!map.Remove(FileRules.Normalize(oldPath), out var existing)) return;
            map[FileRules.Normalize(newPath)] = existing;
            ++_revision;
        }
    }

    public void Prune(string source, IEnumerable<string> present) =>
        PruneNormalized(source, present.Select(FileRules.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>Keeps only <paramref name="present"/>, whose paths are already normalized.</summary>
    internal void PruneNormalized(string source, IReadOnlySet<string> present)
    {
        lock (_gate)
        {
            if (!_sources.TryGetValue(source, out var map)) return;
            List<string>? gone = null;
            foreach (var path in map.Keys) if (!present.Contains(path)) (gone ??= []).Add(path);
            if (gone is null) return; // The common case: nothing left the folder, so nothing is written either.
            foreach (var path in gone) if (map.Remove(path)) ++_revision;
        }
    }

    public void KeepSources(IEnumerable<string> sources)
    {
        var ids = sources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var source in _sources.Keys.Where(id => !ids.Contains(id)).ToArray())
                if (_sources.Remove(source, out var map) && map.Count > 0) ++_revision;
    }

    public async Task SaveAsync()
    {
        // Concurrent callers queue here; once the first has written, the rest see their revision already on disk.
        await _writeGate.WaitAsync();
        try
        {
            List<IndexEntry> snapshot;
            long revision;
            lock (_gate)
            {
                revision = _revision;
                if (revision == _savedRevision) return;
                snapshot = new List<IndexEntry>(_sources.Values.Sum(map => map.Count));
                foreach (var (source, map) in _sources)
                    foreach (var (path, date) in map) snapshot.Add(new(source, path, date));
            }
            await AtomicJson.WriteAsync(_path, new IndexData(1, snapshot), AtomicJson.Compact);
            // Changes made during the write keep a higher revision, so the next save picks them up.
            lock (_gate) _savedRevision = revision;
        }
        finally { _writeGate.Release(); }
    }
}

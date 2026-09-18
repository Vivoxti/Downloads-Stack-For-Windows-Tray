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
    private readonly Dictionary<string, IndexEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    // A scan runs whenever a watched folder changes; without this, every one of them rewrote the whole file.
    private long _revision, _savedRevision;
    private static string Key(string source, string path) => source + "\0" + FileRules.Normalize(path);
    public IndexStore(string? directory = null) => _path = Path.Combine(directory ?? LocalLog.DataDirectory, "index.json");
    /// <summary>Changes since startup. Equal values mean the in-memory index and the file agree.</summary>
    internal long Revision { get { lock (_gate) return _revision; } }

    public async Task<string?> LoadAsync() => await Task.Run(() =>
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var data = JsonSerializer.Deserialize<IndexData>(File.ReadAllText(_path), AtomicJson.Options);
            if (data is null || data.SchemaVersion != 1 || data.Entries is null) throw new JsonException(Loc.T("Error_IndexShape"));
            var validated = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in data.Entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.SourceId) || string.IsNullOrWhiteSpace(entry.Path) ||
                    !Path.IsPathFullyQualified(entry.Path) || entry.EffectiveDateUtc.Kind != DateTimeKind.Utc)
                    throw new JsonException(Loc.T("Error_IndexEntry"));
                validated[Key(entry.SourceId, entry.Path)] = entry;
            }
            // Loading is not a change: the file already holds exactly this.
            lock (_gate) foreach (var pair in validated) _entries[pair.Key] = pair.Value;
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            LocalLog.Write("Index recovery", ex);
            // Mark dirty so the damaged file is replaced, instead of being backed up again on every start.
            lock (_gate) { _entries.Clear(); ++_revision; }
            try { AtomicJson.Backup(_path); } catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException) { LocalLog.Write("Index backup", backupError); }
            return Loc.T("Error_IndexCorrupt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LocalLog.Write("Read index", ex);
            return Loc.T("Error_IndexRead", ex.Message);
        }
    });

    public DateTime GetOrAdd(string source, string path, DateTime creationUtc, bool initialScan)
    {
        lock (_gate)
        {
            var key = Key(source, path);
            if (_entries.TryGetValue(key, out var existing)) return existing.EffectiveDateUtc;
            var date = initialScan ? creationUtc : DateTime.UtcNow;
            _entries[key] = new(source, FileRules.Normalize(path), date);
            ++_revision;
            return date;
        }
    }

    public void Rename(string source, string oldPath, string newPath)
    {
        lock (_gate)
        {
            if (_entries.Remove(Key(source, oldPath), out var existing))
            {
                _entries[Key(source, newPath)] = existing with { Path = FileRules.Normalize(newPath) };
                ++_revision;
            }
        }
    }

    public void Prune(string source, IEnumerable<string> present)
    {
        var paths = present.Select(FileRules.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var entry in _entries.Values.Where(e => e.SourceId == source && !paths.Contains(e.Path)).ToArray())
                if (_entries.Remove(Key(entry.SourceId, entry.Path))) ++_revision;
    }

    public void KeepSources(IEnumerable<string> sources)
    {
        var ids = sources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var entry in _entries.Values.Where(e => !ids.Contains(e.SourceId)).ToArray())
                if (_entries.Remove(Key(entry.SourceId, entry.Path))) ++_revision;
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
                snapshot = _entries.Values.ToList();
            }
            await AtomicJson.WriteAsync(_path, new IndexData(1, snapshot), AtomicJson.Compact);
            // Changes made during the write keep a higher revision, so the next save picks them up.
            lock (_gate) _savedRevision = revision;
        }
        finally { _writeGate.Release(); }
    }
}

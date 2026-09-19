using System.IO;
using System.Text.Json;
using DownloadsStack.Localization;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

public sealed class SettingsStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1);
    public SettingsStore(string? directory = null) => _path = Path.Combine(directory ?? LocalLog.DataDirectory, "settings.json");

    public async Task<(SettingsData Data, string? Message)> LoadAsync()
    {
        var loaded = await Task.Run(() =>
        {
            if (!File.Exists(_path)) return (Data: new SettingsData(), Message: (string?)null, Save: true);
            try
            {
                var data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path), AtomicJson.Options);
                if (data is null || data.SchemaVersion != 1 || data.Sources is null ||
                    data.Sources.Any(s => s is null || string.IsNullOrWhiteSpace(s.Id) ||
                        (s.Kind != "downloads" && s.Kind != "directory") ||
                        (s.Kind == "directory" && (string.IsNullOrWhiteSpace(s.Path) || !Path.IsPathFullyQualified(s.Path)))) ||
                    data.Sources.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Sources.Count)
                    throw new JsonException(Loc.T("Error_SettingsShape"));
                // A number outside the slider's range is not a damaged file: bring it back in rather than
                // throwing the user's folder list away over it.
                if (data.BackdropOpacity is < 0 or > 100) data = data with { BackdropOpacity = Math.Clamp(data.BackdropOpacity, 0, 100) };
                if (data.MaxVisibleItems < SettingsData.MinVisibleItems || data.MaxVisibleItems > SettingsData.MaxVisibleItemsLimit)
                    data = data with { MaxVisibleItems = Math.Clamp(data.MaxVisibleItems, SettingsData.MinVisibleItems, SettingsData.MaxVisibleItemsLimit) };
                return (Data: data, Message: (string?)null, Save: false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                LocalLog.Write("Settings recovery", ex);
                try
                {
                    var backup = AtomicJson.Backup(_path);
                    return (Data: new SettingsData(), Message: (string?)Loc.T("Error_SettingsCorrupt", backup), Save: true);
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    // If the damaged file cannot be preserved, never overwrite it: the user may still recover it by hand.
                    LocalLog.Write("Settings backup", backupError);
                    return (Data: new SettingsData(), Message: (string?)Loc.T("Error_SettingsRead", backupError.Message), Save: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or unreadable file is not a damaged one. Start on defaults and leave it alone.
                LocalLog.Write("Read settings", ex);
                return (Data: new SettingsData(), Message: (string?)Loc.T("Error_SettingsRead", ex.Message), Save: false);
            }
        });
        if (loaded.Save)
            try { await SaveAsync(loaded.Data); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LocalLog.Write("Write settings", ex);
                return (loaded.Data, string.Join("\n", new[] { loaded.Message, Loc.T("Error_Settings", ex.Message) }.Where(m => m is not null)));
            }
        return (loaded.Data, loaded.Message);
    }

    public async Task SaveAsync(SettingsData data)
    {
        await _writeGate.WaitAsync();
        try { await AtomicJson.WriteAsync(_path, data); }
        finally { _writeGate.Release(); }
    }
}

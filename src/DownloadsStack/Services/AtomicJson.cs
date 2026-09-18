using System.IO;
using System.Text.Json;

namespace DownloadsStack.Services;

internal static class AtomicJson
{
    internal static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    /// <summary>The index holds one entry per file on disk; indentation would triple a large user's file for nobody to read.</summary>
    internal static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    public static Task WriteAsync<T>(string path, T value, JsonSerializerOptions? options = null) => Task.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, options ?? Options);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });

    public static string Backup(string path)
    {
        var backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..6];
        File.Copy(path, backup, false);
        return backup;
    }
}

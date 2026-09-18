using System.IO;
using System.Text;
using DownloadsStack.Interop;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

public static class FileRules
{
    private static readonly string[] TemporaryEndings = [".crdownload", ".part", ".partial", ".tmp"];
    public static bool Include(string name, FileAttributes attributes) =>
        (attributes & (FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System)) == 0 &&
        !TemporaryEndings.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static string ResolvePath(string path)
    {
        var normalized = Normalize(path);
        using var handle = NativeMethods.CreateFileW(normalized, 0, 1 | 2 | 4, 0, 3, 0x02000000, 0);
        if (handle.IsInvalid) return normalized;
        var buffer = new StringBuilder(512);
        var length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer.EnsureCapacity((int)length + 1);
            length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0) return normalized;
        var result = buffer.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) result = @"\\" + result[8..];
        else if (result.StartsWith(@"\\?\", StringComparison.Ordinal)) result = result[4..];
        return Normalize(result);
    }

    public static IReadOnlyList<DownloadItem> Merge(IEnumerable<DownloadItem> files, int limit = 100)
    {
        var result = files.OrderByDescending(f => f.EffectiveDateUtc)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(f => f.CanonicalPath, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
        var duplicates = result.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return result.Select(file => new DownloadItem
        {
            SourceId = file.SourceId, FullPath = file.FullPath, CanonicalPath = file.CanonicalPath,
            Name = file.Name, EffectiveDateUtc = file.EffectiveDateUtc, LastWriteTimeUtc = file.LastWriteTimeUtc,
            DuplicateName = duplicates.Contains(file.Name)
        }).ToArray();
    }
}

using System.IO;
using System.Text;
using DownloadsStack.Interop;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

public static class FileRules
{
    private static readonly string[] TemporaryEndings = [".crdownload", ".part", ".partial", ".tmp"];
    private const FileAttributes Excluded = FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System;

    /// <summary>
    /// Takes the name as a span. A scan tests every entry of the folder, and the enumerator hands the name
    /// out without building a string, so an unchanged folder never allocates one here.
    /// </summary>
    public static bool Include(ReadOnlySpan<char> name, FileAttributes attributes)
    {
        if ((attributes & Excluded) != 0) return false;
        foreach (var ending in TemporaryEndings)
            if (name.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

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

    public static IReadOnlyList<DownloadItem> Merge(IEnumerable<DownloadItem> files, FileOrder order = default, int limit = 100)
    {
        var sorted = files.ToArray();
        Array.Sort(sorted, (first, second) => Compare(first.Key, second.Key, order));
        var result = sorted.DistinctBy(f => f.CanonicalPath, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
        var duplicates = result.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return result.Select(file => new DownloadItem
        {
            SourceId = file.SourceId, FullPath = file.FullPath, CanonicalPath = file.CanonicalPath,
            Name = file.Name, EffectiveDateUtc = file.EffectiveDateUtc, LastWriteTimeUtc = file.LastWriteTimeUtc,
            CreationTimeUtc = file.CreationTimeUtc, LastAccessTimeUtc = file.LastAccessTimeUtc,
            DuplicateName = duplicates.Contains(file.Name)
        }).ToArray();
    }

    /// <summary>
    /// The ordering <see cref="Merge"/> applies, exposed so a source can drop everything past the global
    /// limit before publishing: a folder of ten thousand files never needs more than the first hundred.
    /// Every field is a total order — ties fall through to the name and then the full path — which is what
    /// lets a source cut its own list before the sources are merged.
    /// </summary>
    internal static int Compare(in SortKey first, in SortKey second, FileOrder order)
    {
        // Dates read newest first and names A to Z, so one flag means the same thing to every field.
        var result = order.Field switch
        {
            SortField.Name => 0,
            // As a span: ranking a large folder must not allocate an extension string per comparison.
            SortField.Type => Path.GetExtension(first.Name.AsSpan()).CompareTo(Path.GetExtension(second.Name.AsSpan()), StringComparison.OrdinalIgnoreCase),
            SortField.DateModified => second.Modified.CompareTo(first.Modified),
            SortField.DateCreated => second.Created.CompareTo(first.Created),
            SortField.DateAccessed => second.Accessed.CompareTo(first.Accessed),
            _ => second.Added.CompareTo(first.Added),
        };
        if (result == 0) result = string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase);
        if (result == 0) result = string.Compare(first.Path, second.Path, StringComparison.OrdinalIgnoreCase);
        return order.Reversed ? -result : result;
    }
}

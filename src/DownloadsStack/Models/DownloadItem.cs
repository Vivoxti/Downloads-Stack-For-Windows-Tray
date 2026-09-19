using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DownloadsStack.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    public required string SourceId { get; init; }
    public required string FullPath { get; init; }
    public required string CanonicalPath { get; init; }
    public required string Name { get; init; }
    public required DateTime EffectiveDateUtc { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public DateTime CreationTimeUtc { get; init; }
    /// <summary>
    /// Read from the directory entry like the others. Windows only maintains it when the volume asks for
    /// it, and nothing here watches for it changing: the order by this field settles on the next scan.
    /// </summary>
    public DateTime LastAccessTimeUtc { get; init; }
    /// <summary>Everything <see cref="Services.FileRules"/> sorts on, without touching the disk again.</summary>
    public SortKey Key => new(EffectiveDateUtc, LastWriteTimeUtc, CreationTimeUtc, LastAccessTimeUtc, Name, FullPath);
    private bool _duplicateName;
    private ImageSource? _icon;
    public bool DuplicateName { get => _duplicateName; set { _duplicateName = value; Changed(); } }
    public ImageSource? Icon { get => _icon; set { _icon = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record IndexEntry(string SourceId, string Path, DateTime EffectiveDateUtc);
/// <summary>
/// One file reduced to what an ordering compares. A struct with no work in it: a source ranks thousands
/// of these per scan, and building one must cost nothing but copying fields that were already read.
/// </summary>
public readonly record struct SortKey(DateTime Added, DateTime Modified, DateTime Created, DateTime Accessed, string Name, string Path);
/// <summary>A struct: a scan holds one of these per file of the folder, and they must not be objects.</summary>
public readonly record struct FileStamp(long Length, DateTime LastWriteTimeUtc);

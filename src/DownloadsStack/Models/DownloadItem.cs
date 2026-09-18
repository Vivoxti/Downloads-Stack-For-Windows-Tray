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
    private bool _duplicateName;
    private ImageSource? _icon;
    public bool DuplicateName { get => _duplicateName; set { _duplicateName = value; Changed(); } }
    public ImageSource? Icon { get => _icon; set { _icon = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record IndexEntry(string SourceId, string Path, DateTime EffectiveDateUtc);
public sealed record FileStamp(long Length, DateTime LastWriteTimeUtc);

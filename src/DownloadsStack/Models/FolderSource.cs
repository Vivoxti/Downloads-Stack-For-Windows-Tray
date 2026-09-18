using System.Text.Json.Serialization;
using DownloadsStack.Localization;

namespace DownloadsStack.Models;

public sealed record FolderSource
{
    [JsonRequired] public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [JsonRequired] public string Kind { get; init; } = "directory";
    public string? Path { get; init; }
    public static FolderSource Downloads() => new() { Kind = "downloads" };
}

public sealed record SettingsData
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public List<FolderSource> Sources { get; init; } = [FolderSource.Downloads()];
}

public enum SourceState { Probing, Ready, Denied, Unavailable }

public sealed record SourceStatus(FolderSource Source, string? ResolvedPath, SourceState State, string? Detail = null)
{
    public string DisplayPath => ResolvedPath ?? Source.Path ?? Loc.T("Source_Downloads");
    public string StatusText => State switch
    {
        SourceState.Ready => Loc.T("Source_Ready"),
        SourceState.Probing => Loc.T("Source_Probing"),
        SourceState.Denied => Loc.T("Source_Denied"),
        _ => Loc.T("Source_Unavailable", Detail ?? ""),
    };
    public bool IsUnavailable => State is SourceState.Denied or SourceState.Unavailable;
    /// <summary>"Available" on every row is noise; only problems and probing are worth a second line.</summary>
    public bool ShowStatus => State != SourceState.Ready;
}

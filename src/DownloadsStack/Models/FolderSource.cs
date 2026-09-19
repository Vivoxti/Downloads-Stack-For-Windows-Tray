using System.Text.Json;
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
    /// <summary>
    /// Whether WPF may render through Direct3D. Not required in the file: a settings file written before
    /// this option existed means the same as a new one, which is the sparing renderer.
    /// </summary>
    public bool HardwareRendering { get; init; }
    /// <summary>
    /// How opaque the dark plate under each file name is, in percent. Zero leaves the name over the bare
    /// desktop, a hundred hides whatever is behind it. Not required in the file either: a settings file
    /// written before this option existed keeps the plate it always had.
    /// </summary>
    public int BackdropOpacity { get; init; } = DefaultBackdropOpacity;
    /// <summary>The value the plate shipped with, before it could be changed.</summary>
    public const int DefaultBackdropOpacity = 85;
    /// <summary>
    /// How many of the newest files the flyout shows at once. The monitor still has the last word: a short
    /// screen shows fewer than this, never more. Not required in the file: a settings file written before
    /// this option existed means the number the panel always fitted, which is ten.
    /// </summary>
    public int MaxVisibleItems { get; init; } = DefaultMaxVisibleItems;
    /// <summary>What the 560 DIP panel fitted at 50 DIP a row, before the count could be chosen.</summary>
    public const int DefaultMaxVisibleItems = 10;
    /// <summary>The ends of the slider. One file is still a list; twenty rows is 1020 DIP, a tall screen's worth.</summary>
    public const int MinVisibleItems = 1, MaxVisibleItemsLimit = 20;
    /// <summary>
    /// What the list is ordered by. Not required in the file: a settings file written before this option
    /// existed means the order the application always had, which is the date a file appeared in its folder.
    /// </summary>
    public SortField SortBy { get; init; } = SortField.DateAdded;
    /// <summary>Turns the whole list end for end, whichever field it is ordered by.</summary>
    public bool SortReversed { get; init; }
    [JsonIgnore] public FileOrder Order => new(SortBy, SortReversed);
}

/// <summary>What the list is sorted by. The first value is the order the application had before the choice existed.</summary>
[JsonConverter(typeof(SortFieldConverter))]
public enum SortField { DateAdded, Name, Type, DateModified, DateCreated, DateAccessed }

/// <summary>
/// Reads the field by name and answers with the default for anything it does not recognise. A file from a
/// newer version, or one edited by hand, must not cost the user their folder list over a single word.
/// </summary>
internal sealed class SortFieldConverter : JsonConverter<SortField>
{
    public override SortField Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Enum.TryParse<SortField>(reader.GetString(), true, out var field) && Enum.IsDefined(field)
            ? field : default;
    public override void Write(Utf8JsonWriter writer, SortField value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>
/// A complete ordering of the list: the field it is keyed on, and whether it is turned end for end. Every
/// date sorts newest first and every name A to Z, so <paramref name="Reversed"/> means the same to all of them.
/// </summary>
public readonly record struct FileOrder(SortField Field, bool Reversed);

/// <summary>One line of the sort dropdown. The catalog key follows the field's own name.</summary>
public sealed record SortOption(SortField Field)
{
    public string Text => Loc.T("Sort_" + Field);
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

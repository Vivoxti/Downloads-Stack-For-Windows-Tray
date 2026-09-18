using System.Globalization;

namespace DownloadsStack.Localization;

/// <summary>Runtime string catalog for the language Windows is set to.</summary>
public static class Loc
{
    public const string FallbackCode = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = Catalog.En, ["ru"] = Catalog.Ru, ["uk"] = Catalog.Uk, ["es"] = Catalog.Es,
        ["pt"] = Catalog.Pt, ["fr"] = Catalog.Fr, ["de"] = Catalog.De, ["it"] = Catalog.It,
        ["pl"] = Catalog.Pl, ["tr"] = Catalog.Tr, ["zh"] = Catalog.Zh, ["ja"] = Catalog.Ja,
        ["ko"] = Catalog.Ko, ["hi"] = Catalog.Hi,
    };

    private static Dictionary<string, string> _active = Catalog.En;
    /// <summary>Catalog in use: the Windows display language, or English when it has none.</summary>
    public static string ActiveCode { get; private set; } = FallbackCode;
    /// <summary>Binding source for XAML: {Binding [Key], Source={x:Static loc:Loc.Text}}.</summary>
    public static LocText Text { get; } = new();

    public static void UseSystemLanguage() => Use(Resolve());

    internal static IReadOnlyCollection<string> Codes => Catalogs.Keys;
    internal static IReadOnlyDictionary<string, string> CatalogFor(string code) => Catalogs[code];
    internal static void Use(string code)
    {
        ActiveCode = Catalogs.ContainsKey(code) ? code : FallbackCode;
        _active = Catalogs[ActiveCode];
    }

    private static string Resolve()
    {
        for (var culture = CultureInfo.CurrentUICulture; culture is not null && culture != CultureInfo.InvariantCulture; culture = culture.Parent)
            if (Catalogs.ContainsKey(culture.TwoLetterISOLanguageName)) return culture.TwoLetterISOLanguageName;
        return FallbackCode;
    }

    public static string T(string key) =>
        _active.TryGetValue(key, out var value) ? value
        : Catalog.En.TryGetValue(key, out var fallback) ? fallback
        : key;

    public static string T(string key, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, T(key), arguments);
}

/// <summary>Indexer wrapper so XAML can look strings up by key.</summary>
public sealed class LocText
{
    internal LocText() { }
    public string this[string key] => Loc.T(key);
}

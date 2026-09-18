using System.Globalization;
using DownloadsStack.Localization;

namespace DownloadsStack.Tests;

[CollectionDefinition("Localization")]
public sealed class LocalizationCollection;

/// <summary>These switch the shared catalog, so they never run beside the tray-menu assertions.</summary>
[Collection("Localization")]
public class LocalizationTests
{
    [Fact]
    public void EveryCatalogCoversTheEnglishKeysWithoutPlaceholders()
    {
        var english = Loc.CatalogFor("en");
        foreach (var code in Loc.Codes)
        {
            var catalog = Loc.CatalogFor(code);
            Assert.Equal(english.Keys.OrderBy(k => k), catalog.Keys.OrderBy(k => k));
            foreach (var key in english.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(catalog[key]), $"{code}/{key} is empty");
                // Formatted strings must keep their argument, or Loc.T would drop the detail.
                Assert.Equal(english[key].Contains("{0}"), catalog[key].Contains("{0}"));
            }
        }
    }

    [Fact]
    public void WindowsLanguageSelectsTheCatalogAndUnknownOnesFallBackToEnglish()
    {
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("uk-UA");
            Loc.UseSystemLanguage();
            Assert.Equal("uk", Loc.ActiveCode);
            Assert.Equal("Вихід", Loc.T("Menu_Exit"));
            CultureInfo.CurrentUICulture = new CultureInfo("de-AT"); // Regional culture resolves through its parent.
            Loc.UseSystemLanguage();
            Assert.Equal("de", Loc.ActiveCode);
            CultureInfo.CurrentUICulture = new CultureInfo("is-IS"); // No catalog anywhere in the chain.
            Loc.UseSystemLanguage();
            Assert.Equal("en", Loc.ActiveCode);
            Assert.Equal("Exit", Loc.T("Menu_Exit"));
        }
        finally { CultureInfo.CurrentUICulture = culture; Loc.UseSystemLanguage(); }
    }
}

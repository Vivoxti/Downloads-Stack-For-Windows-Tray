using System.IO;
using DownloadsStack.Models;
using DownloadsStack.Services;

namespace DownloadsStack.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task MissingSettingsCreateDownloadsButSavedEmptyListStaysEmpty()
    {
        using var folder = new TestDirectory(); var store = new SettingsStore(folder.Path);
        var first = await store.LoadAsync(); Assert.Single(first.Data.Sources); Assert.Equal("downloads", first.Data.Sources[0].Kind);
        await store.SaveAsync(new() { Sources = [] });
        var next = await new SettingsStore(folder.Path).LoadAsync(); Assert.Empty(next.Data.Sources);
    }
    [Fact]
    public async Task RenderPreferenceRoundTripsAndAnOlderFileMeansTheSparingRenderer()
    {
        using var folder = new TestDirectory();
        // Written before the option existed. Reading it must not switch the graphics card back on, and it
        // must not count as damaged either.
        folder.File("settings.json", "{\"schemaVersion\":1,\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.False(loaded.Data.HardwareRendering);
        Assert.Empty(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"));
        await new SettingsStore(folder.Path).SaveAsync(loaded.Data with { HardwareRendering = true });
        var again = await new SettingsStore(folder.Path).LoadAsync();
        Assert.True(again.Data.HardwareRendering);
        Assert.Single(again.Data.Sources);
    }
    [Fact]
    public async Task BackdropOpacityRoundTripsAndAnOlderFileKeepsThePlateItAlwaysHad()
    {
        using var folder = new TestDirectory();
        folder.File("settings.json", "{\"schemaVersion\":1,\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.Equal(SettingsData.DefaultBackdropOpacity, loaded.Data.BackdropOpacity);
        await new SettingsStore(folder.Path).SaveAsync(loaded.Data with { BackdropOpacity = 0 });
        var again = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Equal(0, again.Data.BackdropOpacity); // Zero is a real answer, not a missing field.
        Assert.Single(again.Data.Sources);
    }
    [Fact]
    public async Task AnOpacityOutsideTheRangeIsBroughtBackInsteadOfCondemningTheFile()
    {
        using var folder = new TestDirectory();
        folder.File("settings.json", "{\"schemaVersion\":1,\"backdropOpacity\":140,\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.Equal(100, loaded.Data.BackdropOpacity);
        Assert.Single(loaded.Data.Sources); // The folder list survives a hand-edited number.
        Assert.Empty(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"));
    }
    [Fact]
    public async Task TheVisibleCountRoundTripsAndAnOlderFileKeepsTheTenRowsThePanelAlwaysFitted()
    {
        using var folder = new TestDirectory();
        folder.File("settings.json", "{\"schemaVersion\":1,\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.Equal(SettingsData.DefaultMaxVisibleItems, loaded.Data.MaxVisibleItems);
        await new SettingsStore(folder.Path).SaveAsync(loaded.Data with { MaxVisibleItems = SettingsData.MaxVisibleItemsLimit });
        var again = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Equal(20, again.Data.MaxVisibleItems);
        Assert.Single(again.Data.Sources);
    }
    [Fact]
    public async Task AVisibleCountOutsideTheRangeIsBroughtBackInsteadOfCondemningTheFile()
    {
        using var folder = new TestDirectory();
        // Zero would leave a panel with nothing in it and no way back to the settings from the list itself.
        folder.File("settings.json", "{\"schemaVersion\":1,\"maxVisibleItems\":0,\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.Equal(SettingsData.MinVisibleItems, loaded.Data.MaxVisibleItems);
        Assert.Single(loaded.Data.Sources); // The folder list survives a hand-edited number.
        Assert.Empty(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"));
    }
    [Fact]
    public async Task TheSortChoiceRoundTripsAndAnOlderFileKeepsTheOrderTheListAlwaysHad()
    {
        using var folder = new TestDirectory();
        folder.File("settings.json", "{\"schemaVersion\":1,\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.Equal(SortField.DateAdded, loaded.Data.SortBy);
        Assert.False(loaded.Data.SortReversed);
        await new SettingsStore(folder.Path).SaveAsync(loaded.Data with { SortBy = SortField.DateAccessed, SortReversed = true });
        // Written by name: the file stays legible, and a number would mean nothing to anyone reading it.
        Assert.Contains("\"sortBy\": \"DateAccessed\"", File.ReadAllText(Path.Combine(folder.Path, "settings.json")));
        var again = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Equal(SortField.DateAccessed, again.Data.SortBy);
        Assert.True(again.Data.SortReversed);
        Assert.Single(again.Data.Sources);
    }
    [Fact]
    public async Task AnUnknownSortFieldFallsBackInsteadOfCondemningTheFile()
    {
        using var folder = new TestDirectory();
        folder.File("settings.json", "{\"schemaVersion\":1,\"sortBy\":\"ByColour\",\"sources\":[{\"id\":\"a\",\"kind\":\"downloads\",\"path\":null}]}");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Null(loaded.Message);
        Assert.Equal(SortField.DateAdded, loaded.Data.SortBy);
        Assert.Single(loaded.Data.Sources); // A word from a newer version is not worth the user's folder list.
        Assert.Empty(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"));
    }
    [Fact]
    public void TheOpacityPercentageIsTheAlphaOfThePlate()
    {
        Assert.Equal(0, Alpha(0));
        Assert.Equal(255, Alpha(100));
        Assert.Equal(217, Alpha(SettingsData.DefaultBackdropOpacity)); // What the plate was before the setting.
        Assert.Equal(255, Alpha(400)); // A caller cannot paint past opaque.
        static byte Alpha(int percent) => ((System.Windows.Media.SolidColorBrush)MainViewModel.BackdropBrushFor(percent)).Color.A;
    }
    [Fact]
    public void TheRenderPreferenceIsWhatDecidesWhetherDirect3DIsUsed()
    {
        var original = System.Windows.Media.RenderOptions.ProcessRenderMode;
        try
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            MainViewModel.ApplyRenderMode(true);
            // Asking for the graphics card must leave WPF alone rather than pin it to anything.
            Assert.Equal(System.Windows.Interop.RenderMode.Default, System.Windows.Media.RenderOptions.ProcessRenderMode);
            MainViewModel.ApplyRenderMode(false);
            Assert.Equal(System.Windows.Interop.RenderMode.SoftwareOnly, System.Windows.Media.RenderOptions.ProcessRenderMode);
        }
        finally { System.Windows.Media.RenderOptions.ProcessRenderMode = original; }
    }
    [Fact]
    public async Task DamagedSettingsAreBackedUpBeforeRestoringDefaults()
    {
        using var folder = new TestDirectory(); folder.File("settings.json", "{ damaged");
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.Single(loaded.Data.Sources); Assert.NotNull(loaded.Message);
        Assert.Equal("{ damaged", File.ReadAllText(Assert.Single(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"))));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"sources\":[{\"kind\":\"downloads\"}]}")]
    public async Task IncompleteSettingsRecoverRatherThanSilentlyInventingConfiguration(string damaged)
    {
        using var folder = new TestDirectory(); folder.File("settings.json", damaged);
        var loaded = await new SettingsStore(folder.Path).LoadAsync();
        Assert.NotNull(loaded.Message); Assert.Single(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"));
    }
    [Fact]
    public async Task DatesSurviveEditingRenameRestartAndUnavailableSource()
    {
        using var folder = new TestDirectory(); var index = new IndexStore(folder.Path);
        var oldDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var path = @"C:\files\old.txt"; var renamed = @"C:\files\renamed.txt";
        Assert.Equal(oldDate, index.GetOrAdd("a", path, oldDate, true));
        Assert.Equal(oldDate, index.GetOrAdd("a", path.ToUpperInvariant(), DateTime.UtcNow, false));
        index.Rename("a", path, renamed);
        Assert.Equal(oldDate, index.GetOrAdd("a", renamed, DateTime.UtcNow, false));
        index.GetOrAdd("offline", @"Z:\files\offline.txt", oldDate, true);
        index.Prune("a", [renamed]); await index.SaveAsync();
        var restarted = new IndexStore(folder.Path); Assert.Null(await restarted.LoadAsync());
        Assert.Equal(oldDate, restarted.GetOrAdd("a", renamed, DateTime.UtcNow, true));
        Assert.Equal(oldDate, restarted.GetOrAdd("offline", @"Z:\files\offline.txt", DateTime.UtcNow, true));
    }
    [Fact]
    public async Task NewLiveFileGetsFirstSeenRatherThanCreationAndMissingIndexUsesCreation()
    {
        using var folder = new TestDirectory(); var index = new IndexStore(folder.Path);
        var creation = DateTime.UtcNow.AddYears(-2); var before = DateTime.UtcNow;
        Assert.InRange(index.GetOrAdd("a", @"C:\files\new.txt", creation, false), before, DateTime.UtcNow);
        Assert.Equal(creation, index.GetOrAdd("a", @"C:\files\startup.txt", creation, true));
        await index.SaveAsync(); Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }
    [Theory]
    [InlineData("{ damaged")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[{\"sourceId\":\"a\",\"path\":\"relative.txt\",\"effectiveDateUtc\":\"2020-01-01T00:00:00Z\"}]}")]
    public async Task CorruptIndexRecoversWithoutChangingFiles(string damaged)
    {
        using var folder = new TestDirectory(); var file = folder.File("user.txt", "unchanged"); folder.File("index.json", damaged);
        var index = new IndexStore(folder.Path); Assert.NotNull(await index.LoadAsync());
        var creation = DateTime.UtcNow.AddDays(-10); Assert.Equal(creation, index.GetOrAdd("a", file, creation, true));
        await index.SaveAsync(); Assert.Equal("unchanged", File.ReadAllText(file));
        Assert.Null(await new IndexStore(folder.Path).LoadAsync());
    }
    [Fact]
    public async Task SuccessfulScanPrunesOnlyItsSourceAndRemovedSourcesArePurged()
    {
        using var folder = new TestDirectory(); var index = new IndexStore(folder.Path); var oldDate = DateTime.UtcNow.AddDays(-20);
        index.GetOrAdd("a", @"C:\one.txt", oldDate, true); index.GetOrAdd("b", @"Z:\offline.txt", oldDate, true);
        index.Prune("a", []); await index.SaveAsync();
        var reload = new IndexStore(folder.Path); await reload.LoadAsync();
        Assert.NotEqual(oldDate, reload.GetOrAdd("a", @"C:\one.txt", DateTime.UtcNow, false));
        Assert.Equal(oldDate, reload.GetOrAdd("b", @"Z:\offline.txt", DateTime.UtcNow, false));
        reload.KeepSources([]); await reload.SaveAsync();
        Assert.DoesNotContain("offline.txt", File.ReadAllText(Path.Combine(folder.Path, "index.json")));
        var purged = new IndexStore(folder.Path); Assert.Null(await purged.LoadAsync());
        Assert.NotEqual(oldDate, purged.GetOrAdd("b", @"Z:\offline.txt", DateTime.UtcNow, false));
    }
    [Fact]
    public async Task UnchangedIndexIsNotRewrittenBySubsequentScans()
    {
        using var folder = new TestDirectory(); var index = new IndexStore(folder.Path);
        var path = Path.Combine(folder.Path, "index.json");
        index.GetOrAdd("a", @"C:\one.txt", DateTime.UtcNow.AddDays(-1), true);
        await index.SaveAsync();
        Assert.True(File.Exists(path));
        // Every scan asked for a save. Rewriting one entry per file on disk, once a second, is the cost.
        File.Delete(path);
        await index.SaveAsync(); await index.SaveAsync();
        Assert.False(File.Exists(path));
        index.GetOrAdd("a", @"C:	wo.txt", DateTime.UtcNow, false);
        await index.SaveAsync();
        Assert.True(File.Exists(path));
        Assert.Contains("two.txt", File.ReadAllText(path));
    }
    [Fact]
    public async Task UnreadableSettingsKeepTheFileAndReportRatherThanResetIt()
    {
        using var folder = new TestDirectory();
        var path = folder.File("settings.json", "{\"schemaVersion\":1,\"sources\":[]}");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = await new SettingsStore(folder.Path).LoadAsync();
            Assert.NotNull(loaded.Message);
            Assert.Empty(Directory.GetFiles(folder.Path, "settings.json.corrupt-*"));
        }
        // A locked file is not a damaged one: it has to survive the failed read untouched.
        Assert.Equal("{\"schemaVersion\":1,\"sources\":[]}", File.ReadAllText(path));
    }
    [Fact]
    public async Task SettingsWriteFailureIsReportedWithoutClaimingSuccess()
    {
        using var folder = new TestDirectory(); var collision = folder.File("not-a-directory");
        await Assert.ThrowsAnyAsync<IOException>(() => new SettingsStore(collision).SaveAsync(new()));
    }
}

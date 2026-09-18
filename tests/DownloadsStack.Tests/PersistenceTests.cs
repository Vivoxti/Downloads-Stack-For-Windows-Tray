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

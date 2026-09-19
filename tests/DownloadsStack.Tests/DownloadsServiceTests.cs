using System.IO;
using System.Threading.Channels;
using DownloadsStack.Models;
using DownloadsStack.Services;

namespace DownloadsStack.Tests;

public class DownloadsServiceTests
{
    private static async Task<DownloadsSnapshot> WaitAsync(ChannelReader<DownloadsSnapshot> reader, Func<DownloadsSnapshot, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (await reader.WaitToReadAsync(timeout.Token))
            while (reader.TryRead(out var snapshot)) if (condition(snapshot)) return snapshot;
        throw new TimeoutException();
    }
    [Fact]
    public async Task WatcherTracksFinalFileRenameEditAndDeleteWhileKeepingDates()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        var file = files.File("original.txt", ""); var creation = DateTime.UtcNow.AddYears(-1); File.SetCreationTimeUtc(file, creation);
        files.File("ignored.CRDOWNLOAD"); Directory.CreateDirectory(Path.Combine(files.Path, "child")); File.WriteAllText(Path.Combine(files.Path, "child", "nested.txt"), "");
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        var source = new FolderSource { Path = files.Path }; service.ApplySources([source]);
        var initial = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && !s.Sources[0].IsUnavailable);
        Assert.Equal(creation, initial.Items[0].EffectiveDateUtc);
        File.AppendAllText(file, "changed");
        var edited = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && s.Items[0].LastWriteTimeUtc != initial.Items[0].LastWriteTimeUtc);
        Assert.Equal(creation, edited.Items[0].EffectiveDateUtc);
        var renamed = Path.Combine(files.Path, "renamed.txt"); File.Move(file, renamed);
        var afterRename = await WaitAsync(channel.Reader, s => s.Items.Any(i => i.Name == "renamed.txt"));
        Assert.Equal(creation, afterRename.Items[0].EffectiveDateUtc);
        var part = files.File("new.part", "data"); var before = DateTime.UtcNow; File.Move(part, Path.Combine(files.Path, "new.txt"));
        var added = await WaitAsync(channel.Reader, s => s.Items.Count == 2);
        Assert.InRange(added.Items.Single(i => i.Name == "new.txt").EffectiveDateUtc, before, DateTime.UtcNow);
        File.Delete(renamed); await WaitAsync(channel.Reader, s => s.Items.Count == 1 && s.Items[0].Name == "new.txt");
    }
    [Fact]
    public async Task ChangingTheOrderResortsWhatIsKnownWithoutReadingTheFolderAgain()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        var older = files.File("b.txt"); var newer = files.File("a.txt");
        File.SetCreationTimeUtc(older, DateTime.UtcNow.AddYears(-2)); File.SetCreationTimeUtc(newer, DateTime.UtcNow.AddYears(-1));
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new FolderSource { Path = files.Path }]);
        var byDate = await WaitAsync(channel.Reader, s => s.Items.Count == 2 && !s.Sources[0].IsUnavailable);
        Assert.Equal(new[] { "a.txt", "b.txt" }, byDate.Items.Select(i => i.Name));
        // This file lands first and is deliberately absent from the snapshot below: a re-sort answers from
        // what the source already holds, so nothing here is read from the folder a second time.
        files.File("c.txt");
        service.ApplyOrder(new(SortField.Name, true));
        var byName = await WaitAsync(channel.Reader, s => s.Items.Count == 2);
        Assert.Equal(new[] { "b.txt", "a.txt" }, byName.Items.Select(i => i.Name));
        // The watcher still has the last word: the new file arrives on its own, in the chosen order.
        var settled = await WaitAsync(channel.Reader, s => s.Items.Count == 3);
        Assert.Equal(new[] { "c.txt", "b.txt", "a.txt" }, settled.Items.Select(i => i.Name));
    }
    [Fact]
    public async Task UnavailableSourceDoesNotHideOthersAndRemovalRetiresInFlightScan()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory(); files.File("ok.txt");
        using var service = new DownloadsService(new(data.Path)); var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        var source = new FolderSource { Path = files.Path }; var missing = new FolderSource { Path = Path.Combine(files.Path, "missing") };
        service.ApplySources([source, missing]);
        var snapshot = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && s.Sources.Any(x => x.Source.Id == missing.Id && x.IsUnavailable));
        Assert.Equal("ok.txt", snapshot.Items[0].Name);
        files.File("race.txt"); service.Refresh(); service.ApplySources([]);
        await WaitAsync(channel.Reader, s => s.Sources.Count == 0 && s.Items.Count == 0);
        await Task.Delay(1600);
        while (channel.Reader.TryRead(out var late)) { Assert.Empty(late.Sources); Assert.Empty(late.Items); }
        Assert.True(File.Exists(Path.Combine(files.Path, "ok.txt")));
    }
    [Fact]
    public async Task RestoredSourceKeepsOldDateAndAddedFolderUsesCreation()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        var sourcePath = Path.Combine(files.Path, "source"); Directory.CreateDirectory(sourcePath);
        var file = Path.Combine(sourcePath, "old.txt"); File.WriteAllText(file, "test"); var date = DateTime.UtcNow.AddYears(-2); File.SetCreationTimeUtc(file, date);
        using var service = new DownloadsService(new(data.Path)); var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        var source = new FolderSource { Path = sourcePath }; service.ApplySources([source]);
        var initial = await WaitAsync(channel.Reader, s => s.Items.Count == 1); Assert.Equal(date, initial.Items[0].EffectiveDateUtc);
        var moved = sourcePath + "-away"; Directory.Move(sourcePath, moved); service.Refresh();
        await WaitAsync(channel.Reader, s => s.Items.Count == 0 && s.Sources[0].IsUnavailable);
        Directory.Move(moved, sourcePath); service.Refresh();
        var restored = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && !s.Sources[0].IsUnavailable); Assert.Equal(date, restored.Items[0].EffectiveDateUtc);
    }
    [Fact]
    public async Task TenThousandFilesHaveOneGlobalLimitAndNoNestedItems()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        var date = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 10000; ++i)
        {
            var file = files.File($"{i:00000}.txt", ""); File.SetCreationTimeUtc(file, date.AddSeconds(i));
        }
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        var watch = System.Diagnostics.Stopwatch.StartNew(); service.ApplySources([new() { Path = files.Path }]);
        var snapshot = await WaitAsync(channel.Reader, s => s.Items.Count == 100);
        Assert.Equal("09999.txt", snapshot.Items[0].Name); Assert.Equal("09900.txt", snapshot.Items[^1].Name);
        Assert.All(snapshot.Items, i => Assert.True(i.EffectiveDateUtc.Year == 2020));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
    }
    [Fact]
    public async Task SettledFolderStopsPublishingSoTheListIsNotRebuiltUnderTheUser()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        files.File("done.txt", "content");
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new() { Path = files.Path }]);
        await WaitAsync(channel.Reader, s => s.Items.Count == 1 && !s.Sources[0].IsUnavailable);
        await Task.Delay(1500); // Let any stability pass already in flight finish.
        while (channel.Reader.TryRead(out _)) { }
        // Every write in a watched folder re-scans. An unchanged result must stay silent, or the flyout
        // regenerates every row while a download is running.
        for (var i = 0; i < 5; ++i) { service.Refresh(); await Task.Delay(120); }
        await Task.Delay(1500);
        Assert.False(channel.Reader.TryRead(out var extra), "an unchanged folder published again: " + extra?.Items.Count);
        files.File("second.txt", "more");
        var updated = await WaitAsync(channel.Reader, s => s.Items.Count == 2);
        Assert.Contains(updated.Items, i => i.Name == "second.txt");
    }
    [Fact]
    public async Task FileStillBeingWrittenDoesNotRepublishUntilItIsFinished()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        files.File("existing.txt", "content");
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new() { Path = files.Path }]);
        await WaitAsync(channel.Reader, s => s.Items.Count == 1);
        await Task.Delay(1500);
        while (channel.Reader.TryRead(out _)) { }
        var chunk = new byte[64 * 1024];
        var download = Path.Combine(files.Path, "download.bin");
        using (var stream = File.Open(download, FileMode.Create, FileAccess.Write, FileShare.Read))
            for (var i = 0; i < 16; ++i) { await stream.WriteAsync(chunk); await stream.FlushAsync(); await Task.Delay(250); }
        // Each write wakes the watcher. While the file is still growing there is nothing new to show, and
        // publishing anyway rebuilt every row of the flyout several times a second for the whole download.
        var noise = new List<DownloadsSnapshot>();
        while (channel.Reader.TryRead(out var snapshot)) noise.Add(snapshot);
        Assert.True(noise.Count == 0, $"{noise.Count} snapshots published while the file was still being written");
        var finished = await WaitAsync(channel.Reader, s => s.Items.Count == 2);
        Assert.Contains(finished.Items, i => i.Name == "download.bin");
    }
    [Fact]
    public async Task ScanOfASettledFolderDoesNotOpenAHandlePerFile()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        for (var i = 0; i < 400; ++i) files.File($"{i:000}.txt", "x");
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new() { Path = files.Path }]);
        await WaitAsync(channel.Reader, s => s.Items.Count == 100);
        await Task.Delay(1500);
        while (channel.Reader.TryRead(out _)) { }
        // Resolving each entry through its own file handle made a rescan cost one CreateFile per file.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; ++i) { service.Refresh(); await Task.Delay(60); }
        await Task.Delay(400);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"ten rescans took {watch.Elapsed}");
    }
    [Fact]
    public async Task RescanningASettledFolderCostsNothingPerFile()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        for (var i = 0; i < 2000; ++i) files.File($"{i:0000}-a-reasonably-long-download-name.bin", "x");
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new() { Path = files.Path }]);
        await WaitAsync(channel.Reader, s => s.Items.Count == 100);
        await Task.Delay(1500);
        while (channel.Reader.TryRead(out _)) { }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalAllocatedBytes(true);
        for (var i = 0; i < 10; ++i) { service.Refresh(); await Task.Delay(80); }
        await Task.Delay(600);
        var allocated = GC.GetTotalAllocatedBytes(true) - before;
        // Every pass used to copy the whole folder into fresh dictionaries, candidates and items: about
        // 3 MB per scan at this size, and a scan runs on every write into a watched folder. A file that has
        // not moved must now cost nothing at all — not even the string of its own name.
        Assert.True(allocated < 1_000_000, $"{allocated / 1024} KB allocated over ten rescans of 2000 files");
    }
    [Fact]
    public async Task TwoCrowdedFoldersStillPublishTheNewestHundredOfBoth()
    {
        using var first = new TestDirectory(); using var second = new TestDirectory(); using var data = new TestDirectory();
        var epoch = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 150; ++i)
        {
            File.SetCreationTimeUtc(first.File($"a{i:000}.txt", ""), epoch.AddMinutes(i * 2));
            File.SetCreationTimeUtc(second.File($"b{i:000}.txt", ""), epoch.AddMinutes(i * 2 + 1));
        }
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new() { Path = first.Path }, new() { Path = second.Path }]);
        // Both sources have to have finished: a snapshot taken while one is still probing holds only the other.
        var snapshot = await WaitAsync(channel.Reader, s => s.Items.Count == 100 && s.Sources.Count == 2 && s.Sources.All(x => x.State == SourceState.Ready));
        // A source now drops everything past the global limit before it publishes anything. The merged list
        // still has to be the newest hundred across sources, not the newest fifty of each.
        Assert.Equal("b149.txt", snapshot.Items[0].Name);
        Assert.Equal("a149.txt", snapshot.Items[1].Name);
        Assert.Equal("a100.txt", snapshot.Items[^1].Name);
        Assert.Equal(50, snapshot.Items.Count(i => i.Name[0] == 'a'));
        Assert.Equal(50, snapshot.Items.Count(i => i.Name[0] == 'b'));
    }
    [Fact]
    public async Task RenameWhileScanIsWaitingForStabilityKeepsOriginalDate()
    {
        using var files = new TestDirectory(); using var data = new TestDirectory();
        var file = files.File("old.txt"); var date = DateTime.UtcNow.AddYears(-1); File.SetCreationTimeUtc(file, date);
        using var service = new DownloadsService(new(data.Path)); var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        service.ApplySources([new() { Path = files.Path }]); await WaitAsync(channel.Reader, s => s.Items.Count == 1);
        File.AppendAllText(file, "edit"); service.Refresh(); await Task.Delay(200);
        var renamed = Path.Combine(files.Path, "renamed.txt"); File.Move(file, renamed);
        var snapshot = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && s.Items[0].Name == "renamed.txt");
        Assert.Equal(date, snapshot.Items[0].EffectiveDateUtc);
    }
}

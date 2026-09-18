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
        var file = files.File("старый.txt", ""); var creation = DateTime.UtcNow.AddYears(-1); File.SetCreationTimeUtc(file, creation);
        files.File("ignored.CRDOWNLOAD"); Directory.CreateDirectory(Path.Combine(files.Path, "child")); File.WriteAllText(Path.Combine(files.Path, "child", "nested.txt"), "");
        using var service = new DownloadsService(new(data.Path));
        var channel = Channel.CreateUnbounded<DownloadsSnapshot>(); service.Updated += s => channel.Writer.TryWrite(s);
        var source = new FolderSource { Path = files.Path }; service.ApplySources([source]);
        var initial = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && !s.Sources[0].IsUnavailable);
        Assert.Equal(creation, initial.Items[0].EffectiveDateUtc);
        File.AppendAllText(file, "changed");
        var edited = await WaitAsync(channel.Reader, s => s.Items.Count == 1 && s.Items[0].LastWriteTimeUtc != initial.Items[0].LastWriteTimeUtc);
        Assert.Equal(creation, edited.Items[0].EffectiveDateUtc);
        var renamed = Path.Combine(files.Path, "готовый.txt"); File.Move(file, renamed);
        var afterRename = await WaitAsync(channel.Reader, s => s.Items.Any(i => i.Name == "готовый.txt"));
        Assert.Equal(creation, afterRename.Items[0].EffectiveDateUtc);
        var part = files.File("new.part", "data"); var before = DateTime.UtcNow; File.Move(part, Path.Combine(files.Path, "new.txt"));
        var added = await WaitAsync(channel.Reader, s => s.Items.Count == 2);
        Assert.InRange(added.Items.Single(i => i.Name == "new.txt").EffectiveDateUtc, before, DateTime.UtcNow);
        File.Delete(renamed); await WaitAsync(channel.Reader, s => s.Items.Count == 1 && s.Items[0].Name == "new.txt");
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

using System.IO;
using DownloadsStack.Models;
using DownloadsStack.Services;

namespace DownloadsStack.Tests;

public class FileRulesTests
{
    [Theory]
    [InlineData("a.CRDOWNLOAD")]
    [InlineData("a.Part")]
    [InlineData("a.partial")]
    [InlineData("a.TMP")]
    public void TemporaryFilesExcluded(string name) => Assert.False(FileRules.Include(name, FileAttributes.Normal));
    [Theory]
    [InlineData(FileAttributes.Directory)]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.System)]
    public void SpecialAttributesExcluded(FileAttributes attributes) => Assert.False(FileRules.Include("file.txt", attributes));
    [Theory]
    [InlineData("Нулевой файл.txt")]
    [InlineData("a.tmp.txt")]
    [InlineData("file")]
    public void OrdinaryNamesIncluded(string name) => Assert.True(FileRules.Include(name, FileAttributes.Normal));
    [Fact]
    public void NormalizePreservesRootsAndTrimsOnlyNonRootSeparator()
    {
        Assert.Equal(@"C:\", FileRules.Normalize(@"C:\"));
        Assert.Equal(@"C:\Files", FileRules.Normalize(@"C:\Files\"));
        Assert.Equal(@"C:\Files\file.txt", FileRules.Normalize(@"C:\Files\..\Files\file.txt"));
    }
    private static DownloadItem Item(string path, DateTime date, string source = "one") => new()
    {
        FullPath = path, CanonicalPath = FileRules.Normalize(path), SourceId = source,
        Name = Path.GetFileName(path), EffectiveDateUtc = date
    };
    [Fact]
    public void MergeAppliesGlobalLimitAfterDeduplicationAndStableTies()
    {
        var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = Enumerable.Range(0, 150).Select(n => Item($@"C:\Files\{n:000}.txt", date.AddMinutes(n), n % 2 == 0 ? "one" : "two")).ToList();
        items.Add(Item(@"c:\FILES\149.txt", date.AddMinutes(149), "three"));
        var merged = FileRules.Merge(items);
        Assert.Equal(100, merged.Count); Assert.Equal("149.txt", merged[0].Name); Assert.Equal("050.txt", merged[^1].Name);
        var ties = FileRules.Merge([Item(@"C:\z\b.txt", date), Item(@"C:\z\a.txt", date), Item(@"C:\a\a.txt", date)]);
        Assert.Equal(new[] { @"C:\a\a.txt", @"C:\z\a.txt", @"C:\z\b.txt" }, ties.Select(i => i.FullPath));
        Assert.True(ties[0].DuplicateName); Assert.True(ties[1].DuplicateName); Assert.False(ties[2].DuplicateName);
    }
    [Fact]
    public void MergeNeverChangesItemsAlreadyBoundToUi()
    {
        var item = Item(@"C:\a\test.txt", DateTime.UtcNow);
        var other = Item(@"C:\b\test.txt", DateTime.UtcNow);
        var merged = FileRules.Merge([item, other]);
        Assert.False(item.DuplicateName); Assert.All(merged, i => Assert.True(i.DuplicateName));
    }
    [Fact]
    public void DirectoryJunctionResolvesToSamePath()
    {
        using var folder = new TestDirectory();
        var actual = Directory.CreateDirectory(Path.Combine(folder.Path, "actual")).FullName;
        var link = Path.Combine(folder.Path, "junction");
        // Directory symlink needs Developer Mode/admin; junction does not. Use native mklink only to create it.
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(actual);
        using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        Assert.Equal(FileRules.ResolvePath(actual), FileRules.ResolvePath(link), StringComparer.OrdinalIgnoreCase);
        Directory.Delete(link); // Delete only the junction itself before temporary tree cleanup.
    }
}

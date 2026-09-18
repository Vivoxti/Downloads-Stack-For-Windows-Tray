using DownloadsStack.Models;
using DownloadsStack.Services;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DownloadsStack.Tests;

public class IconServiceTests
{
    [Fact]
    public async Task SystemIconIsFrozenAndSameExtensionSharesCache()
    {
        using var files = new TestDirectory(); using var icons = new IconService();
        DownloadItem Item(string name) => new()
        {
            SourceId = "test", FullPath = files.File(name), CanonicalPath = System.IO.Path.Combine(files.Path, name),
            Name = name, EffectiveDateUtc = DateTime.UtcNow
        };
        var first = await icons.GetAsync(Item("a.txt")).WaitAsync(TimeSpan.FromSeconds(10));
        var second = await icons.GetAsync(Item("b.txt")).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(first.IsFrozen); Assert.Same(first, second); Assert.NotSame(icons.Fallback, first);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    public async Task PicturesUseTheirOwnContentRatherThanSharedExtensionIcon(string extension)
    {
        using var files = new TestDirectory(); using var icons = new IconService();
        string Picture(string name, System.Drawing.Color color)
        {
            var path = Path.Combine(files.Path, name + extension);
            using var bitmap = new System.Drawing.Bitmap(160, 90);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(color);
            bitmap.Save(path, extension == ".png" ? System.Drawing.Imaging.ImageFormat.Png : System.Drawing.Imaging.ImageFormat.Jpeg);
            return path;
        }
        var redItem = Item(Picture("red", System.Drawing.Color.Red));
        var red = await icons.GetAsync(redItem).WaitAsync(TimeSpan.FromSeconds(10));
        var blue = await icons.GetAsync(Item(Picture("blue", System.Drawing.Color.Blue))).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(red.IsFrozen); Assert.True(blue.IsFrozen);
        Assert.NotSame(red, blue);
        Assert.Same(red, await icons.GetAsync(redItem));
        AssertRedThumbnail(red);
        var pixel = CenterPixel(blue);
        Assert.True(pixel[0] > 200 && pixel[1] < 40 && pixel[2] < 40);
    }

    [Fact]
    public async Task Mp4UsesVideoFrameFromWindowsThumbnailProvider()
    {
        using var icons = new IconService();
        var image = await icons.GetAsync(Item(Path.Combine(AppContext.BaseDirectory, "Fixtures", "red.mp4")))
            .WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(image.IsFrozen);
        AssertRedThumbnail(image);
    }

    [Fact]
    public async Task BrokenMp4FallsBackToFileIcon()
    {
        using var files = new TestDirectory(); using var icons = new IconService();
        var image = await icons.GetAsync(Item(files.File("broken.mp4"))).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(image.IsFrozen);
        Assert.NotSame(icons.Fallback, image);
        Assert.Equal(24, Assert.IsAssignableFrom<BitmapSource>(image).PixelWidth);
    }

    [Fact]
    public async Task TransparentPictureKeepsItsAlphaAndItsOrientation()
    {
        using var files = new TestDirectory(); using var icons = new IconService();
        var path = Path.Combine(files.Path, "alpha.png");
        using (var bitmap = new System.Drawing.Bitmap(160, 160))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.FillRectangle(System.Drawing.Brushes.Red, 80, 0, 80, 80);
                graphics.FillRectangle(System.Drawing.Brushes.Blue, 80, 80, 80, 80);
            }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        var image = await icons.GetAsync(Item(path)).WaitAsync(TimeSpan.FromSeconds(15));
        var converted = new FormatConvertedBitmap(Assert.IsAssignableFrom<BitmapSource>(image), PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        byte[] At(double x, double y)
        {
            var pixel = new byte[4];
            converted.CopyPixels(new System.Windows.Int32Rect((int)(converted.PixelWidth * x), (int)(converted.PixelHeight * y), 1, 1), pixel, 4, 0);
            return pixel;
        }
        // A bitmap handle carried through the shell can lose its alpha, which paints a downloaded logo
        // onto a black tile, and a bottom-up copy of it would arrive upside down.
        Assert.Equal(0, At(0.25, 0.5)[3]);
        Assert.True(At(0.75, 0.25) is [_, _, > 200, 255], "the top half must stay red");
        Assert.True(At(0.75, 0.75) is [> 200, _, _, 255], "the bottom half must stay blue");
    }

    [Fact]
    public async Task RequestsAfterShutdownAnswerWithTheFallbackInsteadOfThrowing()
    {
        using var files = new TestDirectory();
        var icons = new IconService();
        Assert.NotSame(icons.Fallback, await icons.GetAsync(Item(files.File("a.txt"))).WaitAsync(TimeSpan.FromSeconds(10)));
        icons.Dispose();
        icons.Dispose(); // Closing twice happens when the window closes during application shutdown.
        // The shell thread and the disposing thread both drained the queue, and the loser threw on the UI.
        Assert.Same(icons.Fallback, await icons.GetAsync(Item(files.File("b.txt"))).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(icons.Fallback, await icons.GetAsync(Item(files.File("c.txt"))).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private static DownloadItem Item(string path) => new()
    {
        SourceId = "test", FullPath = path, CanonicalPath = path, Name = Path.GetFileName(path),
        EffectiveDateUtc = DateTime.UtcNow, LastWriteTimeUtc = File.GetLastWriteTimeUtc(path)
    };

    private static void AssertRedThumbnail(ImageSource image)
    {
        var bitmap = Assert.IsAssignableFrom<BitmapSource>(image);
        Assert.True(bitmap.PixelWidth > 24);
        var pixel = CenterPixel(image);
        Assert.True(pixel[2] > 200 && pixel[1] < 40 && pixel[0] < 40);
    }

    private static byte[] CenterPixel(ImageSource image)
    {
        var bitmap = new FormatConvertedBitmap(Assert.IsAssignableFrom<BitmapSource>(image), PixelFormats.Bgra32, null, 0);
        bitmap.Freeze();
        var pixel = new byte[4];
        bitmap.CopyPixels(new System.Windows.Int32Rect(bitmap.PixelWidth / 2, bitmap.PixelHeight / 2, 1, 1), pixel, 4, 0);
        return pixel;
    }
}

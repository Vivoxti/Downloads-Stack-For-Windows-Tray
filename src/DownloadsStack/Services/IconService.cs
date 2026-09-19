using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DownloadsStack.Interop;
using DownloadsStack.Models;

namespace DownloadsStack.Services;

internal sealed class IconService : IDisposable
{
    private sealed record Request(string Key, string Path, bool ByType, bool Thumbnail, TaskCompletionSource<ImageSource> Completion);
    private readonly BlockingCollection<Request> _queue = new(128);
    private readonly ConcurrentDictionary<string, Task<ImageSource>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _cacheOrder = new();
    private readonly object _gate = new();
    private bool _disposed;
    private bool _stopped; // The shell thread is gone; every further request answers with the fallback.
    public ImageSource Fallback { get; }

    public IconService()
    {
        var fallback = new DrawingImage(new GeometryDrawing(Brushes.LightSteelBlue, new Pen(Brushes.SlateGray, 1), Geometry.Parse("M 4,1 L 14,1 19,6 19,23 4,23 Z M 14,1 L 14,6 19,6")));
        fallback.Freeze(); Fallback = fallback;
        var thread = new Thread(Work) { IsBackground = true, Name = "Shell icons (STA)" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    /// <summary>Files whose own icon differs from every other file of the same kind, so no cache by extension.</summary>
    private static readonly string[] OwnIcon = [".exe", ".lnk", ".ico", ".url"];
    private static readonly string[] OwnThumbnail = [".png", ".jpg", ".jpeg", ".mp4"];
    public Task<ImageSource> GetAsync(DownloadItem item)
    {
        var extension = System.IO.Path.GetExtension(item.FullPath);
        var thumbnail = IsThumbnailFile(item.FullPath);
        var byType = !thumbnail && !Matches(extension, OwnIcon);
        var key = byType ? "type|" + extension : "file|" + item.CanonicalPath + "|" + item.LastWriteTimeUtc.Ticks;
        lock (_gate)
        {
            if (_disposed || _stopped) return Task.FromResult(Fallback);
            if (_cache.TryGetValue(key, out var image)) return Task.FromResult(image);
            if (_pending.TryGetValue(key, out var pending)) return pending;
            var completion = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[key] = completion.Task;
            var queued = false;
            // A full queue or a collection torn down by a racing shutdown must never reach the caller.
            try { queued = _queue.TryAdd(new(key, byType ? "file" + extension : item.FullPath, byType, thumbnail, completion)); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { LocalLog.Write("Icon queue", ex); }
            if (!queued) { _pending.TryRemove(key, out _); completion.SetResult(Fallback); }
            return completion.Task;
        }
    }
    private static bool Matches(ReadOnlySpan<char> extension, string[] known)
    {
        foreach (var candidate in known)
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    internal static bool IsThumbnailFile(string path) => Matches(System.IO.Path.GetExtension(path.AsSpan()), OwnThumbnail);

    private static ImageSource? GetThumbnail(string path)
    {
        IShellItemImageFactory? factory = null;
        nint handle = 0;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (NativeMethods.CreateShellImageFactory(path, 0, in iid, out factory) < 0) return null;
            // THUMBNAILONLY: failures fall back to the existing file icon. 96 px stays sharp on hover/high DPI.
            if (factory.GetImage(new NativeMethods.Size { Width = 96, Height = 96 }, 0x08, out handle) < 0 || handle == 0) return null;
            var image = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            if (handle != 0) NativeMethods.DeleteObject(handle);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }
    private void Work()
    {
        var initialized = false;
        try { initialized = NativeMethods.OleInitialize(0) >= 0; }
        catch (Exception ex) { LocalLog.Write("Icon thread start", ex); }
        try
        {
            foreach (var request in _queue.GetConsumingEnumerable())
            {
                var image = Fallback; nint icon = 0;
                try
                {
                    ImageSource? thumbnail = null;
                    if (request.Thumbnail)
                        try { thumbnail = GetThumbnail(request.Path); }
                        catch (Exception ex) { LocalLog.Write("Shell thumbnail", ex); }
                    if (thumbnail is not null) image = thumbnail;
                    else
                    {
                        // LARGEICON rather than SMALLICON: a 16 px source was stretched to the 24 px row,
                        // and again to 32 px by the hover animation.
                        var result = NativeMethods.SHGetFileInfoW(request.Path, 0x80, out var info, (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(), 0x100 | (request.ByType ? 0x10u : 0u));
                        icon = info.Icon;
                        if (result != 0 && icon != 0)
                        {
                            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(24, 24));
                            bitmap.Freeze(); image = bitmap;
                        }
                    }
                }
                catch (Exception ex) { LocalLog.Write("Shell icon", ex); }
                finally { if (icon != 0) NativeMethods.DestroyIcon(icon); }
                lock (_gate)
                {
                    // Enqueue the eviction key only for a genuinely new entry, or a refreshed one would
                    // push an unrelated icon out of the cache.
                    if (!_cache.ContainsKey(request.Key)) _cacheOrder.Enqueue(request.Key);
                    _cache[request.Key] = image;
                    while (_cache.Count > 256 && _cacheOrder.TryDequeue(out var old)) _cache.Remove(old);
                    _pending.TryRemove(request.Key, out _);
                }
                request.Completion.TrySetResult(image);
            }
        }
        catch (Exception ex) { LocalLog.Write("Icon thread", ex); }
        finally
        {
            // Never dispose the queue here: Dispose() may be draining it on the UI thread right now.
            // Whatever is still queued would otherwise leave its awaiter hanging on a dead thread.
            lock (_gate)
            {
                _stopped = true;
                while (_queue.TryTake(out var abandoned)) abandoned.Completion.TrySetResult(Fallback);
                _pending.Clear();
            }
            if (initialized) try { NativeMethods.OleUninitialize(); } catch (Exception ex) { LocalLog.Write("Icon thread exit", ex); }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
            while (_queue.TryTake(out var request)) request.Completion.TrySetResult(Fallback);
            _pending.Clear();
        }
        // A stuck third-party shell handler must not block process exit.
    }
}

using System.IO;

namespace DownloadsStack.Tests;

internal sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DownloadsStack.Tests", Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public string File(string name, string content = "test")
    {
        var path = System.IO.Path.Combine(Path, name); System.IO.File.WriteAllText(path, content); return path;
    }
    public void Dispose()
    {
        // The fixed test root and unique leaf are checked before recursive cleanup.
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DownloadsStack.Tests")) + System.IO.Path.DirectorySeparatorChar;
        var target = System.IO.Path.GetFullPath(Path);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup target.");
        try { Directory.Delete(target, true); } catch (IOException) { }
    }
}

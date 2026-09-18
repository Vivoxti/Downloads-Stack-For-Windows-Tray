using System.IO;

namespace DownloadsStack.Services;

internal static class LocalLog
{
    private static readonly object Gate = new();
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DownloadsStack");
    public static void Write(string operation, Exception exception)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                var path = Path.Combine(DataDirectory, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTime.UtcNow:O} {operation}: {exception}\n");
            }
            catch (Exception) { /* Logging must never interrupt the application. */ }
        }
    }
}

using System;
using System.IO;

namespace TaskbarBatteryWidget;

internal static class DiagnosticLog
{
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarBatteryWidget", "diagnostics.log");

    public static void Write(string operation, Exception exception)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                // Keep at most two small files, even when a device fails every poll.
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= 256 * 1024)
                    File.Move(LogPath, LogPath + ".old", overwrite: true);

                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:O} | {operation} | {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Diagnostics must never prevent polling or application shutdown.
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TaskbarBatteryWidget;

internal static class RazerLogReader
{
    public static int? ReadBattery(string directory, int productId, DateTime now)
    {
        if (!Directory.Exists(directory)) return null;

        // Never fall back to another product's log when this PID has no log.
        var files = Directory.GetFiles(directory, $"products_{productId}_mw*.log")
            .OrderByDescending(File.GetLastWriteTime);
        foreach (string file in files)
        {
            try
            {
                TimeSpan age = now - File.GetLastWriteTime(file);
                if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(30)) continue;

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(Math.Max(0, stream.Length - 16384), SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var matches = Regex.Matches(reader.ReadToEnd(), @"""level"":\s*(-?\d+)");
                if (matches.Count > 0 &&
                    int.TryParse(matches[^1].Groups[1].Value, out int percent) &&
                    percent is >= 0 and <= 100)
                    return percent;
            }
            catch (Exception exception)
            {
                DiagnosticLog.Write("Razer pil günlüğü okuma", exception);
            }
        }
        return null;
    }
}

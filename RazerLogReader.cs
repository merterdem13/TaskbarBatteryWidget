using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TaskbarBatteryWidget;

internal static class RazerLogReader
{
    private static readonly Regex LevelRegex = new(@"""level"":\s*(-?\d+)", RegexOptions.Compiled);
    private static readonly Regex DisconnectedRegex = new(
        @"""(?:isConnected|connected)"":\s*false|""(?:connectionState|state|powerState)"":\s*""(?:disconnected|off|offline)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(10)) continue;

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(Math.Max(0, stream.Length - 16384), SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string tail = reader.ReadToEnd();

                var matches = LevelRegex.Matches(tail);
                if (matches.Count == 0) continue;

                Match lastMatch = matches[^1];
                if (!int.TryParse(lastMatch.Groups[1].Value, out int percent))
                    continue;

                // In Razer Synapse logs, level <= 0 indicates the wireless device is turned off / disconnected.
                if (percent is <= 0 or > 100)
                    return null;

                // If a disconnect/off event was logged after the last battery level entry, treat as offline.
                string afterLastLevel = tail[(lastMatch.Index + lastMatch.Length)..];
                if (DisconnectedRegex.IsMatch(afterLastLevel))
                    return null;

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

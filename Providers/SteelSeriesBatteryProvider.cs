using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HidSharp;

namespace TaskbarBatteryWidget.Providers;

public class SteelSeriesBatteryProvider : IBatteryProvider
{
    public string ProviderName => "SteelSeries Wireless HID";

    private const int STEELSERIES_VID = 0x1038;

    private readonly HashSet<int> _b0Pids = new()
    {
        0x220E, // Arctis 7+
        0x2212, // Arctis 7P+
        0x2216, // Arctis 7X+
        0x2236, // Arctis 7+ Destiny
        0x2202, // Arctis Nova 7
        0x2206, // Arctis Nova 7P
        0x220A, // Arctis Nova 7X
        0x2258, // Arctis Nova 7 Dragon
        0x2232, // Arctis Nova 5
        0x2253  // Arctis Nova 5X
    };

    public SteelSeriesBatteryProvider()
    {
        LoadExternalDefinitions();
    }

    private void LoadExternalDefinitions()
    {
        try
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_definitions.json");
            if (File.Exists(jsonPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (doc.RootElement.TryGetProperty("steelseries", out var ss) &&
                    ss.TryGetProperty("b0_pids", out var pids))
                {
                    foreach (var elem in pids.EnumerateArray())
                    {
                        if (elem.TryGetInt32(out int pid))
                        {
                            _b0Pids.Add(pid);
                        }
                    }
                }
            }
        }
        catch { }
    }

    public List<BatteryDeviceInfo> DiscoverDevices()
    {
        return PollInternal();
    }

    public List<BatteryDeviceInfo> PollStatus(IReadOnlyList<BatteryDeviceInfo> knownDevices)
    {
        return PollInternal();
    }

    private List<BatteryDeviceInfo> PollInternal()
    {
        var list = new List<BatteryDeviceInfo>();
        try
        {
            var ssDevices = DeviceList.Local.GetHidDevices(STEELSERIES_VID).ToList();
            var groupedByPid = ssDevices.GroupBy(d => d.ProductID).ToList();

            foreach (var group in groupedByPid)
            {
                int pid = group.Key;
                bool isB0Model = _b0Pids.Contains(pid);
                bool useLegacy = !isB0Model && (pid == 0x12AD || pid == 0x1294 || pid == 0x1260 || pid == 0x1252);

                if (!isB0Model && !useLegacy) continue;

                // Doğrudan mi_03 arayüzünü ara; eğer bulamazsa yazılabilir (MaxOutputReportLength >= 2) ilk arayüzü seç
                var targetDev = group.FirstOrDefault(d => d.DevicePath.ToLowerInvariant().Contains("mi_03"))
                    ?? group.FirstOrDefault(d =>
                    {
                        string p = d.DevicePath.ToLowerInvariant();
                        return (p.Contains("&col03") || p.Contains("col04") || d.GetMaxOutputReportLength() >= 2) && !p.Contains("mi_04");
                    })
                    ?? group.First();

                string name = "";
                try { name = targetDev.GetProductName()?.Trim() ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(name))
                    name = isB0Model ? "SteelSeries Nova/Arctis" : "SteelSeries Arctis";

                if (targetDev.TryOpen(out var stream))
                {
                    using (stream)
                    {
                        stream.ReadTimeout = 150;
                        stream.WriteTimeout = 150;

                        byte[] outCmd = isB0Model ? new byte[] { 0x00, 0xB0 } : new byte[] { 0x06, 0x18 };
                        stream.Write(outCmd);

                        byte[] inBuf = new byte[64];
                        int read = stream.Read(inBuf, 0, inBuf.Length);

                        if (isB0Model && read >= 4)
                        {
                            byte state = inBuf[2];
                            byte rawLevel = inBuf[3];

                            // state == 0x01: Kulaklık kapalı (Dongle takılı ama kulaklık kapalı/bağlantı yok)
                            if (state == 0x01) continue;

                            int pct = rawLevel switch
                            {
                                <= 4 => Math.Clamp(rawLevel * 25, 5, 100),
                                _ => Math.Clamp((int)rawLevel, 1, 100)
                            };

                            list.Add(new BatteryDeviceInfo
                            {
                                Id = $"ss_{pid:X4}",
                                Name = name,
                                Type = DeviceType.Headset,
                                BatteryPercent = pct,
                                IsCharging = (state == 0x02)
                            });
                        }
                        else if (useLegacy && read >= 3)
                        {
                            int pct = Math.Clamp((int)inBuf[2], 0, 100);
                            if (pct > 0)
                            {
                                list.Add(new BatteryDeviceInfo
                                {
                                    Id = $"ss_{pid:X4}",
                                    Name = name,
                                    Type = DeviceType.Headset,
                                    BatteryPercent = pct,
                                    IsCharging = false
                                });
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return list;
    }
}

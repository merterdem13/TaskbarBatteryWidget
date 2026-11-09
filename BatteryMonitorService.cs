using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace TaskbarBatteryWidget;

public enum DeviceType
{
    Mouse,
    Headset,
    Earbuds,
    Phone
}

public class BatteryDeviceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; }
    public int? BatteryPercent { get; set; }
    public bool IsCharging { get; set; }
    public string StatusText => BatteryPercent.HasValue
        ? (IsCharging ? $"⚡%{BatteryPercent.Value}" : $"%{BatteryPercent.Value}")
        : (IsCharging ? "⚡Şarjda" : "Bağlı");
}

public static class BatteryMonitorService
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    // Known SteelSeries Wireless Headset PIDs using the modern 0xB0 protocol (Arctis 7+, 7P+, 7X+, Nova 7, Nova 7P, Nova 7X, Nova 5, etc.)
    private static readonly HashSet<int> SteelSeriesB0Pids = new()
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

    public static List<BatteryDeviceInfo> PollAllDevices()
    {
        var result = new List<BatteryDeviceInfo>();

        // 1. Poll ALL connected Razer Wireless Devices (VID 0x1532)
        result.AddRange(PollAllRazerDevices());

        // 2. Poll ALL connected SteelSeries Wireless Headsets (VID 0x1038)
        result.AddRange(PollAllSteelSeriesHeadsets());

        // 3. Poll AirPods / Bluetooth Headsets, Mice, Gamepads & Generic HID Battery Devices
        var btDevices = PollBluetoothAndGenericDevices(result);
        result.AddRange(btDevices);

        // 4. Poll iPhone / iPad via USB (VID 0x05AC)
        var iphone = PollIPhoneUsb();
        if (iphone != null)
            result.Add(iphone);

        return result;
    }

    private static List<BatteryDeviceInfo> PollAllRazerDevices()
    {
        var list = new List<BatteryDeviceInfo>();
        try
        {
            var razerGroups = DeviceList.Local.GetHidDevices(0x1532)
                .GroupBy(d => d.ProductID)
                .ToList();

            foreach (var group in razerGroups)
            {
                int pid = group.Key;
                var target = group.FirstOrDefault(d => d.GetMaxFeatureReportLength() >= 90);
                if (target == null) continue;

                string friendlyName = GetFriendlyNameOrDefault(target, $"Razer Cihazı (0x{pid:X4})");
                DeviceType devType = GuessRazerDeviceType(friendlyName, group);

                int? pct = null;
                bool isCharging = false;

                using var handle = CreateFile(target.DevicePath, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (!handle.IsInvalid)
                {
                    foreach (byte txId in new byte[] { 0x1F, 0x3F, 0xFF })
                    {
                        byte[] req = new byte[91];
                        req[0] = 0x00; // Report ID 0
                        req[1] = 0x00; // Status
                        req[2] = txId; // Transaction ID
                        req[6] = 0x02; // Data size 2
                        req[7] = 0x07; // Power Class
                        req[8] = 0x80; // Get Battery Level (0x80)
                        byte crc = 0;
                        for (int i = 3; i < 89; i++) crc ^= req[i];
                        req[89] = crc;

                        if (HidD_SetFeature(handle, req, req.Length))
                        {
                            Thread.Sleep(40);
                            byte[] resp = new byte[91];
                            resp[0] = 0x00;
                            if (HidD_GetFeature(handle, resp, resp.Length) && resp[1] == 0x02)
                            {
                                int rawVal = resp[10];
                                if (rawVal > 0)
                                {
                                    pct = Math.Clamp((int)Math.Round(rawVal * 100.0 / 255.0), 1, 100);
                                    break;
                                }
                            }
                        }
                    }

                    // Also try querying charging status (0x84) if supported
                    if (pct.HasValue)
                    {
                        byte[] chgReq = new byte[91];
                        chgReq[2] = 0x1F;
                        chgReq[6] = 0x02;
                        chgReq[7] = 0x07;
                        chgReq[8] = 0x84; // Get Charging Status
                        byte crc = 0;
                        for (int i = 3; i < 89; i++) crc ^= chgReq[i];
                        chgReq[89] = crc;

                        if (HidD_SetFeature(handle, chgReq, chgReq.Length))
                        {
                            Thread.Sleep(30);
                            byte[] chgResp = new byte[91];
                            if (HidD_GetFeature(handle, chgResp, chgResp.Length) && chgResp[1] == 0x02)
                            {
                                isCharging = chgResp[10] == 1;
                            }
                        }
                    }
                }

                // Fallback to Razer Synapse 4 log for this PID
                if (!pct.HasValue)
                {
                    pct = ReadRazerBatteryFromLog(pid);
                }

                if (pct.HasValue && pct.Value > 0)
                {
                    list.Add(new BatteryDeviceInfo
                    {
                        Id = $"razer_{pid:X4}",
                        Name = friendlyName,
                        Type = devType,
                        BatteryPercent = pct.Value,
                        IsCharging = isCharging
                    });
                }
            }
        }
        catch { }

        return list;
    }

    private static string GetFriendlyNameOrDefault(HidDevice dev, string fallback)
    {
        try
        {
            string name = dev.GetProductName();
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }
        catch { }
        return fallback;
    }

    private static DeviceType GuessRazerDeviceType(string name, IEnumerable<HidDevice> interfaces)
    {
        if (name.Contains("Kraken", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("BlackShark", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Barracuda", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Headset", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Headset;
        }
        return DeviceType.Mouse;
    }

    private static int? ReadRazerBatteryFromLog(int pid)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Razer\RazerAppEngine\User Data\Logs");
            if (!Directory.Exists(logDir)) return null;

            var mwLogs = Directory.GetFiles(logDir, $"products_{pid}_mw*.log");
            if (mwLogs.Length == 0)
                mwLogs = Directory.GetFiles(logDir, "products_*_mw*.log");

            foreach (var logFile in mwLogs.OrderByDescending(File.GetLastWriteTime))
            {
                if ((DateTime.Now - File.GetLastWriteTime(logFile)).TotalMinutes > 30)
                    continue;

                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long startPos = Math.Max(0, fs.Length - 16384);
                fs.Seek(startPos, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                string tail = sr.ReadToEnd();

                var matches = Regex.Matches(tail, @"""level"":\s*(\d+)");
                if (matches.Count > 0)
                {
                    if (int.TryParse(matches[^1].Groups[1].Value, out int level) && level > 0)
                        return level;
                }
            }
        }
        catch { }
        return null;
    }

    private static List<BatteryDeviceInfo> PollAllSteelSeriesHeadsets()
    {
        var list = new List<BatteryDeviceInfo>();
        try
        {
            var ssGroups = DeviceList.Local.GetHidDevices(0x1038)
                .GroupBy(d => d.ProductID)
                .ToList();

            foreach (var group in ssGroups)
            {
                int pid = group.Key;
                var target = group.FirstOrDefault(d => d.DevicePath.Contains("mi_03", StringComparison.OrdinalIgnoreCase) && d.GetMaxOutputReportLength() >= 31)
                          ?? group.FirstOrDefault(d => d.GetMaxOutputReportLength() >= 31);

                if (target == null) continue;

                string name = GetFriendlyNameOrDefault(target, $"SteelSeries (0x{pid:X4})");

                if (target.TryOpen(out var stream))
                {
                    using (stream)
                    {
                        stream.ReadTimeout = 250;
                        stream.WriteTimeout = 250;

                        // Modern SteelSeries protocol (Arctis 7+, Nova 5/7, etc.) uses [0x00, 0xB0]
                        // Legacy Arctis 7 (2019) / Pro uses [0x06, 0x18]
                        bool useLegacy = !SteelSeriesB0Pids.Contains(pid) && (pid == 0x12AD || pid == 0x1260 || pid == 0x1252);

                        byte[] outBuf = new byte[target.GetMaxOutputReportLength()];
                        outBuf[0] = useLegacy ? (byte)0x06 : (byte)0x00;
                        outBuf[1] = useLegacy ? (byte)0x18 : (byte)0xB0;
                        stream.Write(outBuf);

                        byte[] inBuf = new byte[target.GetMaxInputReportLength()];
                        int read = stream.Read(inBuf, 0, inBuf.Length);

                        if (!useLegacy && read >= 5 && inBuf[1] == 0xB0)
                        {
                            byte state = inBuf[2]; // 0x01 = Offline, 0x02 = Charging, 0x03 = Connected
                            byte rawBatt = inBuf[3];

                            if (state == 0x01)
                                continue; // Headset is turned OFF

                            int pct = rawBatt <= 4 ? Math.Clamp(rawBatt * 25, 0, 100) : Math.Clamp((int)rawBatt, 0, 100);
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

    private static List<BatteryDeviceInfo> PollBluetoothAndGenericDevices(List<BatteryDeviceInfo> alreadyFound)
    {
        var list = new List<BatteryDeviceInfo>();
        try
        {
            foreach (var bt in NativeBluetoothBatteryReader.GetConnectedBluetoothBatteries())
            {
                // Skip if it's the BLE profile of an already-found USB dongle mouse/headset
                if (bt.Name.Contains("DA V2 X", StringComparison.OrdinalIgnoreCase) &&
                    alreadyFound.Any(d => d.Type == DeviceType.Mouse))
                {
                    continue;
                }

                if (alreadyFound.Any(d => d.Name.Equals(bt.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                list.Add(new BatteryDeviceInfo
                {
                    Id = "bt_" + bt.Name,
                    Name = bt.Name,
                    Type = bt.Type,
                    BatteryPercent = bt.BatteryPercent,
                    IsCharging = false
                });
            }
        }
        catch { }
        return list;
    }

    private static BatteryDeviceInfo? PollIPhoneUsb()
    {
        try
        {
            var info = NativeBluetoothBatteryReader.GetConnectedIPhoneInfo();
            if (info != null)
            {
                return new BatteryDeviceInfo
                {
                    Id = "iphone_usb",
                    Name = info.Value.Name,
                    Type = DeviceType.Phone,
                    BatteryPercent = info.Value.BatteryPercent,
                    IsCharging = true
                };
            }
        }
        catch { }
        return null;
    }
}

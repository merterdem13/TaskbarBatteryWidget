using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace TaskbarBatteryWidget.Providers;

public class RazerBatteryProvider : IBatteryProvider
{
    public string ProviderName => "Razer Wireless HID";

    private const int RAZER_VID = 0x1532;

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

    public List<BatteryDeviceInfo> DiscoverDevices()
    {
        return PollInternal();
    }

    public List<BatteryDeviceInfo> PollStatus(IReadOnlyList<BatteryDeviceInfo> knownDevices)
    {
        return PollInternal();
    }

    private static List<BatteryDeviceInfo> PollInternal()
    {
        var list = new List<BatteryDeviceInfo>();
        try
        {
            var razerGroups = DeviceList.Local.GetHidDevices(RAZER_VID)
                .GroupBy(d => d.ProductID)
                .ToList();

            foreach (var group in razerGroups)
            {
                int pid = group.Key;
                var target = group.FirstOrDefault(d => d.GetMaxFeatureReportLength() >= 90);
                if (target == null) continue;

                string friendlyName = GetFriendlyNameOrDefault(target, $"Razer Cihazı (0x{pid:X4})");
                DeviceType devType = GuessRazerDeviceType(friendlyName);

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

    private static DeviceType GuessRazerDeviceType(string name)
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
}

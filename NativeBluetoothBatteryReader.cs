using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Netimobiledevice;
using Netimobiledevice.Lockdown;
using Netimobiledevice.Usbmuxd;

namespace TaskbarBatteryWidget;

public enum AirPodsPart
{
    None,
    Case,
    Left,
    Right
}

public sealed class BluetoothBatteryInfo
{
    public string Name { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public int? BatteryPercent { get; init; }
    public DeviceType Type { get; init; }
    public AirPodsPart AirPodsPart { get; init; }
}

public static class NativeBluetoothBatteryReader
{
    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_ALLCLASSES = 0x00000004;
    private const int CR_SUCCESS = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    // DEVPKEY_Device_FriendlyName: {a45c254e-df1c-4efd-8020-67d146a850e0}, 14
    private static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };

    // DEVPKEY_Device_DeviceDesc: {a45c254e-df1c-4efd-8020-67d146a850e0}, 2
    private static readonly DEVPROPKEY DEVPKEY_Device_DeviceDesc = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 2
    };

    // DEVPKEY_Device_InstanceId: {78c34fc8-104a-4aca-9ea4-524d52996e57}, 256
    private static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new()
    {
        fmtid = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"),
        pid = 256
    };

    // Battery Level Property: {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2
    private static readonly DEVPROPKEY DEVPKEY_Bluetooth_Battery = new()
    {
        fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"),
        pid = 2
    };

    // Bluetooth Actively Connected Property: {2BD67D8B-8BEB-48D5-87E0-6CDA3428040A}, 11 (DEVPKEY_Bluetooth_DeviceConnected)
    private static readonly DEVPROPKEY DEVPKEY_Bluetooth_Connected = new()
    {
        fmtid = new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"),
        pid = 11
    };

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr ClassGuid,
        string? Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr DeviceInfoSet,
        uint MemberIndex,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        byte[]? PropertyBuffer,
        uint PropertyBufferSize,
        out uint RequiredSize,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("cfgmgr32.dll", SetLastError = false)]
    private static extern int CM_Get_Parent(
        out uint pdnDevInst,
        uint dnDevInst,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        byte[]? PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(
        out uint pulLen,
        string? pszFilter,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(
        string? pszFilter,
        [Out] char[] buffer,
        uint bufferLen,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        uint ulFlags);

    public static List<BluetoothBatteryInfo> GetConnectedBluetoothBatteries()
    {
        var cfgMgrCandidates = ReadBluetoothBatteryDevNodes();
        if (cfgMgrCandidates.Count > 0)
            return CollapseAirPodsCandidates(cfgMgrCandidates);

        var candidates = new List<BluetoothBatteryInfo>();
        var seenNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string enumName in new[] { "BTHENUM", "BTHLE", "HID" })
        {
            IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, enumName, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
                continue;

            try
            {
                uint index = 0;
                SP_DEVINFO_DATA devData = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

                while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devData))
                {
                    index++;

                    string instanceId = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_InstanceId) ?? "";
                    string? name = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_FriendlyName)
                                ?? GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_DeviceDesc);
                    string ancestorName = GetBluetoothAncestorName(devData.DevInst);
                    bool isAirPods = IsAirPodsInstance(instanceId, name, ancestorName);
                    byte? batt = GetByteProperty(hDevInfo, ref devData, DEVPKEY_Bluetooth_Battery);
                    if (batt != null && (batt.Value == 0 || batt.Value > 100))
                        continue;

                    // Verify active connection on this node or its parent Bluetooth device node.
                    // Child HID/GATT nodes often omit DEVPKEY_Bluetooth_Connected while their parent
                    // BTHLE/BTHENUM node explicitly reports false when turned off or disconnected.
                    if (!IsBluetoothDeviceActivelyConnected(hDevInfo, ref devData) && !isAirPods)
                        continue;

                    name ??= string.IsNullOrWhiteSpace(ancestorName)
                        ? (isAirPods ? "AirPods" : null)
                        : ancestorName;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    name = CleanDeviceName(name);
                    if (isAirPods)
                        name = NormalizeAirPodsName(name, ancestorName);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    DeviceType type = GuessGenericDeviceType(name);
                    if (batt == null && !isAirPods)
                        continue;
                    AirPodsPart airPodsPart = ParseAirPodsPart(name, instanceId);
                    string dedupKey = airPodsPart == AirPodsPart.None
                        ? name
                        : $"{RemoveAirPodsPartMarker(name)}|{airPodsPart}";

                    if (seenNodes.Add(dedupKey))
                    {
                        candidates.Add(new BluetoothBatteryInfo
                        {
                            Name = name,
                            InstanceId = instanceId,
                            BatteryPercent = batt,
                            Type = type,
                            AirPodsPart = airPodsPart
                        });
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
        }

        return CollapseAirPodsCandidates(candidates);
    }

    private static List<BluetoothBatteryInfo> ReadBluetoothBatteryDevNodes()
    {
        const uint filterEnumerator = 0x00000001;
        const uint filterPresent = 0x00000100;
        var byAddress = new Dictionary<string, BluetoothBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (string enumerator in new[] { "BTHENUM", "BTHLE" })
        {
            if (CM_Get_Device_ID_List_SizeW(out uint size, enumerator, filterEnumerator | filterPresent) != CR_SUCCESS ||
                size <= 1)
                continue;

            var buffer = new char[size];
            if (CM_Get_Device_ID_ListW(enumerator, buffer, size, filterEnumerator | filterPresent) != CR_SUCCESS)
                continue;

            foreach (string instanceId in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                string? address = ExtractBluetoothAddress(instanceId);
                if (address == null || CM_Locate_DevNodeW(out uint devInst, instanceId, 0) != CR_SUCCESS)
                    continue;

                int? battery = GetDevInstByteProperty(devInst, DEVPKEY_Bluetooth_Battery);
                if (!battery.HasValue || battery is < 0 or > 100)
                    continue;

                string name = GetDevInstStringProperty(devInst, DEVPKEY_Device_FriendlyName)
                           ?? GetDevInstStringProperty(devInst, DEVPKEY_Device_DeviceDesc)
                           ?? "";
                string ancestorName = GetBluetoothAncestorName(devInst);
                bool isAirPods = IsAirPodsInstance(instanceId, name, ancestorName);
                if (string.IsNullOrWhiteSpace(name) && !isAirPods)
                    continue;

                name = CleanDeviceName(string.IsNullOrWhiteSpace(name) ? ancestorName : name);
                if (isAirPods)
                    name = NormalizeAirPodsName(name, ancestorName);

                AirPodsPart part = ParseAirPodsPart(name, instanceId);
                string key = $"{address}|{part}";
                if (!byAddress.TryGetValue(key, out BluetoothBatteryInfo? current) ||
                    (!current.BatteryPercent.HasValue && battery.HasValue))
                {
                    byAddress[key] = new BluetoothBatteryInfo
                    {
                        Name = name,
                        InstanceId = instanceId,
                        BatteryPercent = battery,
                        Type = GuessGenericDeviceType(name),
                        AirPodsPart = part
                    };
                }
            }
        }

        return byAddress.Values.ToList();
    }

    private static int? GetDevInstByteProperty(uint devInst, DEVPROPKEY key)
    {
        byte[] buffer = new byte[4];
        uint size = (uint)buffer.Length;
        int result = CM_Get_DevNode_PropertyW(devInst, ref key, out uint propertyType, buffer, ref size, 0);
        if (result != CR_SUCCESS || size == 0)
            return null;

        return propertyType switch
        {
            0x03 when size >= 1 => buffer[0],
            0x08 when size >= 4 => BitConverter.ToInt32(buffer, 0),
            _ => null
        };
    }

    private static string? ExtractBluetoothAddress(string instanceId)
    {
        Match match = Regex.Match(
            instanceId,
            @"(?:DEV_|&)([0-9A-F]{12})(?:_C|\\|$)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    internal static AirPodsPart ParseAirPodsPart(string name, string instanceId)
    {
        if (!IsAirPodsName(name))
            return AirPodsPart.None;

        string value = $"{name} {instanceId}";
        if (ContainsAny(value, "charging case", "charge case", "case", "kutu"))
            return AirPodsPart.Case;
        if (ContainsAny(value, "left", " sol", "_l", "-l", "\\l"))
            return AirPodsPart.Left;
        if (ContainsAny(value, "right", " sağ", " sag", "_r", "-r", "\\r"))
            return AirPodsPart.Right;
        return AirPodsPart.None;
    }

    internal static string RemoveAirPodsPartMarker(string name)
    {
        string result = name;
        foreach (string marker in new[]
        {
            "Charging Case", "Charge Case", "(Left)", "(Right)", "(Sol)", "(Sağ)", "(Sag)",
            "Case", "Kutu", "Left", "Right", "Sol", "Sağ", "Sag"
        })
        {
            result = result.Replace(marker, "", StringComparison.OrdinalIgnoreCase);
        }

        return string.Join(" ", result.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .TrimEnd('-', '_', ':')
            .Trim();
    }

    internal static string NormalizeAirPodsName(string name, string ancestorName)
    {
        string normalized = IsAirPodsName(name) ? name : ancestorName;
        if (IsAirPodsName(normalized))
        {
            foreach (string suffix in new[]
            {
                " Hands-Free AG Audio", " Hands-Free AG", " Hands-Free",
                " Avrcp Transport", " AVRCP Transport", " AAP Server",
                " Hizmet Bulma Hizmeti", " Service"
            })
            {
                normalized = normalized.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);
            }

            return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        return "AirPods";
    }

    internal static List<BluetoothBatteryInfo> CollapseAirPodsCandidates(List<BluetoothBatteryInfo> candidates)
    {
        var explicitAirPods = candidates
            .Where(candidate => candidate.AirPodsPart != AirPodsPart.None)
            .GroupBy(candidate => RemoveAirPodsPartMarker(candidate.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (explicitAirPods.Count == 0)
        {
            return candidates
                .Where(candidate => !IsAirPodsName(candidate.Name))
                .Concat(candidates
                    .Where(candidate => IsAirPodsName(candidate.Name))
                    .GroupBy(candidate => RemoveAirPodsPartMarker(candidate.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(candidate => candidate.BatteryPercent.HasValue)
                        .First()))
                .ToList();
        }

        var explicitNames = new HashSet<string>(
            explicitAirPods.Select(group => group.Key),
            StringComparer.OrdinalIgnoreCase);
        var result = candidates
            .Where(candidate =>
                candidate.AirPodsPart == AirPodsPart.None &&
                explicitNames.Contains(RemoveAirPodsPartMarker(candidate.Name)) == false)
            .ToList();

        foreach (var group in explicitAirPods)
        {
            foreach (var part in group
                .GroupBy(candidate => candidate.AirPodsPart)
                .Select(partGroup => partGroup.First()))
            {
                result.Add(part);
            }
        }

        return result;
    }

    private static bool IsAirPodsName(string name) =>
        name.Contains("AirPods", StringComparison.OrdinalIgnoreCase);

    private static bool IsAirPodsInstance(string instanceId, string? name, string ancestorName)
    {
        return (name?.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ?? false) ||
               ancestorName.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
               instanceId.Contains("VID&02068E_PID&009D", StringComparison.OrdinalIgnoreCase) ||
               instanceId.Contains("VID&0001004C_PID&2013", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetBluetoothAncestorName(uint devInst)
    {
        uint currentInst = devInst;
        for (int depth = 0; depth < 6; depth++)
        {
            if (CM_Get_Parent(out uint parentInst, currentInst, 0) != CR_SUCCESS || parentInst == 0)
                break;

            string? parentName = GetDevInstStringProperty(parentInst, DEVPKEY_Device_FriendlyName)
                             ?? GetDevInstStringProperty(parentInst, DEVPKEY_Device_DeviceDesc);
            if (!string.IsNullOrWhiteSpace(parentName))
                return CleanDeviceName(parentName);

            currentInst = parentInst;
        }

        return "";
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool IsBluetoothDeviceActivelyConnected(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData)
    {
        bool? directConnected = GetBoolProperty(hDevInfo, ref devData, DEVPKEY_Bluetooth_Connected);
        if (directConnected.HasValue)
            return directConnected.Value;

        // Walk up the parent device tree (e.g. HID -> BTHLEDevice -> BTHLE\Dev_xxx)
        uint currentInst = devData.DevInst;
        for (int depth = 0; depth < 6; depth++)
        {
            if (CM_Get_Parent(out uint parentInst, currentInst, 0) != CR_SUCCESS || parentInst == 0)
                break;

            bool? parentConnected = GetDevInstBoolProperty(parentInst, DEVPKEY_Bluetooth_Connected);
            if (parentConnected.HasValue)
                return parentConnected.Value;

            currentInst = parentInst;
        }

        // If neither the node nor any ancestor exposes DEVPKEY_Bluetooth_Connected,
        // do not assume an unverified cached HID/BLE node is actively connected.
        return false;
    }

    internal static string CleanDeviceName(string name)
    {
        string cleaned = name.Replace("Hands-Free AG Audio", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (cleaned.EndsWith(" Stereo", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^" Stereo".Length].Trim();
        return cleaned;
    }

    internal static DeviceType GuessGenericDeviceTypeForTest(string name) => GuessGenericDeviceType(name);

    private static DeviceType GuessGenericDeviceType(string name)
    {
        if (name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fare", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Viper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DeathAdder", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Basilisk", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Naga", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Orochi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cobra", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Superlight", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("G Pro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DA V2", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Mouse;
        }

        if (name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Buds", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Earbuds", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Hammerhead", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Earbuds;
        }

        if (name.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("iPod", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Galaxy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Xperia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("OnePlus", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Huawei", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Redmi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("POCO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("OPPO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("vivo", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Nokia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Motorola", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Moto ", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SM-", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Phone;
        }

        return DeviceType.Headset;
    }

    public static (string Name, int? BatteryPercent, bool IsCharging)? GetConnectedIPhoneInfo()
    {
        IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
            return null;

        try
        {
            uint index = 0;
            SP_DEVINFO_DATA devData = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

            while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devData))
            {
                index++;

                string? instanceId = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_InstanceId);
                if (instanceId == null || !instanceId.Contains("VID_05AC", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Apple iPhone PIDs typically start with PID_12A8, PID_12AB, etc., or FriendlyName contains iPhone/Apple Mobile Device
                string? name = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_FriendlyName)
                            ?? GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_DeviceDesc)
                            ?? "iPhone";

                if (name.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("iPad", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase) ||
                    instanceId.Contains("PID_12A", StringComparison.OrdinalIgnoreCase))
                {
                    string displayName = name.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase) ? "iPhone" : name;
                    var battery = TryReadIPhoneBattery();
                    return (displayName, battery?.BatteryPercent, battery?.IsCharging == true);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
    }

    private static (int BatteryPercent, bool IsCharging)? TryReadIPhoneBattery()
    {
        try
        {
            foreach (var device in Usbmux.GetDeviceList().Where(d => d.ConnectionType == UsbmuxdConnectionType.Usb))
            {
                try
                {
                    using var lockdown = MobileDevice.CreateUsingUsbmux(device.Serial, autopair: false);
                    int? percent = ReadBatteryInteger(lockdown, "BatteryCurrentCapacity");
                    bool? charging = ReadBatteryBoolean(lockdown, "BatteryIsCharging");
                    if (percent.HasValue && percent.Value is >= 0 and <= 100)
                        return (percent.Value, charging == true);
                }
                catch (Exception exception)
                {
                    // A device can be present but not trusted yet; keep the USB presence result.
                    DiagnosticLog.Write("iPhone pil bilgisi", exception);
                }
            }
        }
        catch (Exception exception)
        {
            // Apple Devices/iTunes or usbmuxd may not be installed or running.
            DiagnosticLog.Write("iPhone usbmux bağlantısı", exception);
        }

        return null;
    }

    private static int? ReadBatteryInteger(UsbmuxLockdownClient lockdown, string key)
    {
        var value = lockdown.GetValue("com.apple.mobile.battery", key)?.AsIntegerNode();
        return value == null ? null : (int)value.Value;
    }

    private static bool? ReadBatteryBoolean(UsbmuxLockdownClient lockdown, string key)
    {
        var value = lockdown.GetValue("com.apple.mobile.battery", key)?.AsBooleanNode();
        return value?.Value;
    }

    private static byte? GetByteProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, DEVPROPKEY key)
    {
        byte[] buf = new byte[4];
        if (SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out uint propType, buf, (uint)buf.Length, out uint reqSize, 0))
        {
            if (reqSize >= 1)
                return buf[0];
        }
        return null;
    }

    private static bool? GetBoolProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, DEVPROPKEY key)
    {
        byte[] buf = new byte[4];
        if (SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out uint propType, buf, (uint)buf.Length, out uint reqSize, 0))
        {
            if (reqSize >= 1)
                return buf[0] != 0;
        }
        return null;
    }

    private static bool? GetDevInstBoolProperty(uint devInst, DEVPROPKEY key)
    {
        byte[] buf = new byte[4];
        uint size = (uint)buf.Length;
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out _, buf, ref size, 0) == CR_SUCCESS && size >= 1)
        {
            return buf[0] != 0;
        }
        return null;
    }

    private static string? GetDevInstStringProperty(uint devInst, DEVPROPKEY key)
    {
        byte[] buffer = new byte[512];
        uint size = (uint)buffer.Length;
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) == CR_SUCCESS &&
            size > 2)
        {
            return Encoding.Unicode.GetString(buffer, 0, (int)size - 2);
        }

        return null;
    }

    private static string? GetStringProperty(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, DEVPROPKEY key)
    {
        byte[] buf = new byte[512];
        if (SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out uint propType, buf, (uint)buf.Length, out uint reqSize, 0))
        {
            if (reqSize > 2)
                return Encoding.Unicode.GetString(buf, 0, (int)reqSize - 2);
        }
        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarBatteryWidget;

public static class NativeBluetoothBatteryReader
{
    private const int DIGCF_PRESENT = 0x00000002;
    private const int DIGCF_ALLCLASSES = 0x00000004;

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
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    private static readonly Guid GUID_DEVINTERFACE_HID = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    /// <summary>
    /// Bluetooth cihazının şu anda aktif bir HID arayüzü olup olmadığını doğrular.
    /// Cihaz kapatıldığında Windows sanal BLE düğümünü tutsa bile HID arayüzü kaybolur.
    /// </summary>
    private static bool IsDeviceActivelyPresent(string devInstanceId)
    {
        // Eğer cihaz ID'si biliniyorsa, sistemde o ID ile ilişkili aktif bir HID arayüzü var mı kontrol et
        try
        {
            Guid hidGuid = GUID_DEVINTERFACE_HID;
            IntPtr hDevInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12); // DIGCF_DEVICEINTERFACE | DIGCF_PRESENT
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
                return true;

            try
            {
                SP_DEVICE_INTERFACE_DATA ifData = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                uint idx = 0;

                // MAC adresini veya aygıt ID parçasını çıkar (örn: DEV_E6F364A0752E)
                string token = "";
                int devIdx = devInstanceId.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
                if (devIdx >= 0)
                {
                    int endIdx = devInstanceId.IndexOf('\\', devIdx);
                    token = endIdx > devIdx ? devInstanceId.Substring(devIdx + 4, endIdx - (devIdx + 4)) : devInstanceId.Substring(devIdx + 4);
                }

                if (string.IsNullOrWhiteSpace(token))
                    return true; // Token çıkarılamazsa engelleme yapma

                while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, idx++, ref ifData))
                {
                    SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0, out uint reqSize, IntPtr.Zero);
                    if (reqSize == 0) continue;

                    IntPtr pDetail = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(pDetail, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, pDetail, reqSize, out _, IntPtr.Zero))
                    {
                        string path = Marshal.PtrToStringAuto(pDetail + 4) ?? "";
                        if (path.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            Marshal.FreeHGlobal(pDetail);
                            return true; // Donanım gerçekten bağlı ve aktif!
                        }
                    }
                    Marshal.FreeHGlobal(pDetail);
                }

                return false; // Eşleşmiş ancak kapalı (aktif HID arayüzü yok)
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
        }
        catch
        {
            return true;
        }
    }

    public static List<(string Name, int BatteryPercent, DeviceType Type)> GetConnectedBluetoothBatteries()
    {
        var results = new List<(string Name, int BatteryPercent, DeviceType Type)>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                    byte? batt = GetByteProperty(hDevInfo, ref devData, DEVPKEY_Bluetooth_Battery);
                    if (batt == null || batt.Value == 0 || batt.Value > 100)
                        continue;

                    // Check if actively connected (if property exists and is false, skip)
                    bool? isConnected = GetBoolProperty(hDevInfo, ref devData, DEVPKEY_Bluetooth_Connected);
                    if (isConnected.HasValue && !isConnected.Value)
                        continue;

                    string? instanceId = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_InstanceId);
                    if (!string.IsNullOrEmpty(instanceId) && enumName == "BTHLE")
                    {
                        // Fare ve klavye gibi BLE cihazlarının gerçekten aktif bir radyo bağlantısı olup olmadığını doğrula
                        if (!IsDeviceActivelyPresent(instanceId))
                            continue;
                    }

                    string? name = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_FriendlyName)
                                ?? GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_DeviceDesc);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // Clean up Hands-Free / Avrcp suffixes
                    name = name.Replace("Hands-Free AG Audio", "", StringComparison.OrdinalIgnoreCase)
                               .Replace("Stereo", "", StringComparison.OrdinalIgnoreCase)
                               .Trim();

                    DeviceType type = GuessGenericDeviceType(name);

                    if (!seenNames.Contains(name))
                    {
                        seenNames.Add(name);
                        results.Add((name, batt.Value, type));
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
        }

        return results;
    }

    private static DeviceType GuessGenericDeviceType(string name)
    {
        if (name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fare", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Viper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DeathAdder", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Basilisk", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Superlight", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("G Pro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DA V2", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Mouse;
        }

        if (name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Buds", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Earbuds", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceType.Earbuds;
        }

        return DeviceType.Headset;
    }

    public static (string Name, int? BatteryPercent)? GetConnectedAndroidPhoneInfo()
    {
        // Common Android Vendor IDs:
        // Samsung (04E8), Xiaomi/Redmi (2717), Google Pixel (18D1), Huawei/Honor (12D1), OnePlus/Oppo (22D9), Vivo (2D95), LG (1004), Motorola (22B8)
        string[] androidVids = new[] { "VID_04E8", "VID_2717", "VID_18D1", "VID_12D1", "VID_22D9", "VID_2D95", "VID_1004", "VID_22B8" };

        foreach (string enumClass in new[] { "WPD", "USB" })
        {
            IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, enumClass, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
                continue;

            try
            {
                uint index = 0;
                SP_DEVINFO_DATA devData = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

                while (SetupDiEnumDeviceInfo(hDevInfo, index++, ref devData))
                {
                    string? instanceId = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_InstanceId);
                    if (string.IsNullOrEmpty(instanceId))
                        continue;

                    // Apple cihazlarını atla (onlar GetConnectedIPhoneInfo ile yakalanıyor)
                    if (instanceId.Contains("VID_05AC", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? name = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_FriendlyName)
                                ?? GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_DeviceDesc);

                    bool isAndroidVid = false;
                    foreach (var vid in androidVids)
                    {
                        if (instanceId.Contains(vid, StringComparison.OrdinalIgnoreCase))
                        {
                            isAndroidVid = true;
                            break;
                        }
                    }

                    bool hasAndroidKeywords = !string.IsNullOrEmpty(name) && (
                        name.Contains("Galaxy", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Redmi", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("OnePlus", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Huawei", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("POCO", StringComparison.OrdinalIgnoreCase)
                    );

                    if (isAndroidVid || (enumClass == "WPD" && hasAndroidKeywords))
                    {
                        string displayName = !string.IsNullOrWhiteSpace(name) ? name.Trim() : "Android Telefon";
                        byte? batt = GetByteProperty(hDevInfo, ref devData, DEVPKEY_Bluetooth_Battery);
                        return (displayName, batt.HasValue && batt.Value <= 100 ? (int)batt.Value : null);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
        }

        return null;
    }

    public static (string Name, int? BatteryPercent)? GetConnectedIPhoneInfo()
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
                    byte? batt = GetByteProperty(hDevInfo, ref devData, DEVPKEY_Bluetooth_Battery);
                    string displayName = name.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase) ? "iPhone" : name;
                    return (displayName, batt.HasValue && batt.Value <= 100 ? (int)batt.Value : null);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return null;
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

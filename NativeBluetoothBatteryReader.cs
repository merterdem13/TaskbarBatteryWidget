using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarBatteryWidget;

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

                    // Verify active connection on this node or its parent Bluetooth device node.
                    // Child HID/GATT nodes often omit DEVPKEY_Bluetooth_Connected while their parent
                    // BTHLE/BTHENUM node explicitly reports false when turned off or disconnected.
                    if (!IsBluetoothDeviceActivelyConnected(hDevInfo, ref devData))
                        continue;

                    string? name = GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_FriendlyName)
                                ?? GetStringProperty(hDevInfo, ref devData, DEVPKEY_Device_DeviceDesc);

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    name = CleanDeviceName(name);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

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

        return DeviceType.Headset;
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
                    // USB presence alone does not expose Apple's battery or charging state.
                    string displayName = name.Contains("Apple Mobile Device", StringComparison.OrdinalIgnoreCase) ? "iPhone" : name;
                    return (displayName, null);
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskbarBatteryWidget.Providers;

public class BluetoothBatteryProvider : IBatteryProvider
{
    public string ProviderName => "Bluetooth & Windows System Devices";

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
            foreach (var bt in NativeBluetoothBatteryReader.GetConnectedBluetoothBatteries())
            {
                list.Add(new BatteryDeviceInfo
                {
                    Id = "bt_" + bt.Name,
                    Name = bt.Name,
                    Type = bt.Type,
                    BatteryPercent = bt.BatteryPercent,
                    IsCharging = false
                });
            }

            var iphone = NativeBluetoothBatteryReader.GetConnectedIPhoneInfo();
            if (iphone != null)
            {
                list.Add(new BatteryDeviceInfo
                {
                    Id = "iphone_usb",
                    Name = iphone.Value.Name,
                    Type = DeviceType.Phone,
                    BatteryPercent = iphone.Value.BatteryPercent,
                    IsCharging = true
                });
            }

            var android = NativeBluetoothBatteryReader.GetConnectedAndroidPhoneInfo();
            if (android != null)
            {
                list.Add(new BatteryDeviceInfo
                {
                    Id = "android_usb",
                    Name = android.Value.Name,
                    Type = DeviceType.Phone,
                    BatteryPercent = android.Value.BatteryPercent,
                    IsCharging = true
                });
            }
        }
        catch { }

        return list;
    }
}

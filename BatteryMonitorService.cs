using System.Collections.Generic;

namespace TaskbarBatteryWidget;

public static class BatteryMonitorService
{
    public static void TriggerDiscovery()
    {
        DeviceDiscoveryEngine.TriggerDiscovery();
    }

    public static List<BatteryDeviceInfo> PollAllDevices()
    {
        return DeviceDiscoveryEngine.PollActiveDevices();
    }
}

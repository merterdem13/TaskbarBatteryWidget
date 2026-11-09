using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TaskbarBatteryWidget;

public static class DeviceDiscoveryEngine
{
    private static readonly List<IBatteryProvider> _providers = new()
    {
        new Providers.RazerBatteryProvider(),
        new Providers.SteelSeriesBatteryProvider(),
        new Providers.BluetoothBatteryProvider()
    };

    private static readonly object _syncRoot = new();
    private static List<BatteryDeviceInfo> _cachedDevices = new();
    private static DateTime _lastDiscoveryTime = DateTime.MinValue;
    private const int DISCOVERY_COOLDOWN_MS = 2500; // Art arda gereksiz taramaları engellemek için cooldown

    /// <summary>
    /// Sisteme bağlı tüm aygıtları tarar ve aktif cihaz profilini günceller.
    /// Sadece ilk açılışta veya USB/BT takıp çıkarma (WM_DEVICECHANGE) olayında çağrılmalıdır.
    /// </summary>
    public static void TriggerDiscovery()
    {
        lock (_syncRoot)
        {
            if ((DateTime.Now - _lastDiscoveryTime).TotalMilliseconds < DISCOVERY_COOLDOWN_MS)
                return;

            _lastDiscoveryTime = DateTime.Now;

            var newFound = new List<BatteryDeviceInfo>();
            foreach (var provider in _providers)
            {
                try
                {
                    var devices = provider.DiscoverDevices();
                    foreach (var dev in devices)
                    {
                        // 1. İsim birebir aynıysa ekleme
                        if (newFound.Any(existing => existing.Name.Equals(dev.Name, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // 2. Çift modlu (2.4GHz Dongle + Bluetooth) donanımlarda Dongle önceliği:
                        // Eğer aynı tipe ait aktif bir dongle zaten pil verisi veriyorsa (örn: Razer veya SteelSeries)
                        // ve Bluetooth listesinde de aynı donanım varsa Bluetooth profilini atla.
                        // Dongle kapalıysa veya fare BT moduna alınmışsa BT profilinin önü açıktır.
                        if (provider is Providers.BluetoothBatteryProvider)
                        {
                            bool hasActiveDongle = newFound.Any(existing =>
                                existing.Type == dev.Type && (existing.Id.StartsWith("razer_") || existing.Id.StartsWith("ss_")));

                            if (hasActiveDongle)
                                continue;
                        }

                        newFound.Add(dev);
                    }
                }
                catch { }
            }

            _cachedDevices = newFound;
        }
    }

    /// <summary>
    /// Periyodik polling çağrısı. Yalnızca keşfedilmiş aktif cihazların durumunu sorgular.
    /// Sıfır CPU ve minimal bellek kullanımı garantiler.
    /// </summary>
    public static List<BatteryDeviceInfo> PollActiveDevices()
    {
        lock (_syncRoot)
        {
            // Eğer henüz hiç tarama yapılmadıysa ilk keşfi yap
            if (_lastDiscoveryTime == DateTime.MinValue)
            {
                TriggerDiscovery();
                return _cachedDevices.ToList();
            }

            var updatedList = new List<BatteryDeviceInfo>();
            foreach (var provider in _providers)
            {
                try
                {
                    var currentDevices = provider.PollStatus(_cachedDevices);
                    foreach (var dev in currentDevices)
                    {
                        if (updatedList.Any(d => d.Name.Equals(dev.Name, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Çift modlu BT/Dongle çakışması kontrolü
                        if (provider is Providers.BluetoothBatteryProvider)
                        {
                            bool hasActiveDongle = updatedList.Any(existing =>
                                existing.Type == dev.Type && (existing.Id.StartsWith("razer_") || existing.Id.StartsWith("ss_")));

                            if (hasActiveDongle)
                                continue;
                        }

                        updatedList.Add(dev);
                    }
                }
                catch { }
            }

            _cachedDevices = updatedList;
            return _cachedDevices.ToList();
        }
    }
}

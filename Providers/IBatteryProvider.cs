using System.Collections.Generic;

namespace TaskbarBatteryWidget;

public interface IBatteryProvider
{
    string ProviderName { get; }

    /// <summary>
    /// Tarama yapar ve bu sağlayıcının desteklediği bağlı cihazları listeler.
    /// Sadece donanım değişikliğinde (WM_DEVICECHANGE) veya ilk açılışta çağrılır.
    /// </summary>
    List<BatteryDeviceInfo> DiscoverDevices();

    /// <summary>
    /// Keşfedilmiş olan cihazların pil ve şarj durumunu hızlıca sorgular.
    /// CPU harcamayacak şekilde optimize mikro-sorgudur.
    /// </summary>
    List<BatteryDeviceInfo> PollStatus(IReadOnlyList<BatteryDeviceInfo> knownDevices);
}

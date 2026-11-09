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

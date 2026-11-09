using System.Globalization;
using TaskbarBatteryWidget;

int checks = 0;
void Check(bool success, string name)
{
    if (!success) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"Başarılı: {name}");
}

CultureInfo originalCulture = CultureInfo.CurrentCulture;
string directory = Path.Combine(Path.GetTempPath(), "TaskbarBatteryWidgetTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var position = new WindowPosition(-1520.75, 980.125);
    foreach (string culture in new[] { "tr-TR", "en-US", "de-DE" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        Check(WindowPosition.TryParse(position.Serialize(), out var loaded) && loaded == position,
            $"Kesirli ve negatif konum {culture} kültüründe korunur");
    }
    Check(WindowPosition.TryParse("10,900", out var legacy) && legacy == new WindowPosition(10, 900),
        "Eski tam sayı konum kaydı okunur");
    foreach (string text in new[] { "", "bozuk", "NaN,20", "10,Infinity", "1,2,3,4" })
        Check(!WindowPosition.TryParse(text, out _), $"Geçersiz konum reddedilir: {text}");

    var phone = new BatteryDeviceInfo { Type = DeviceType.Phone, ConnectionText = "USB ile bağlı" };
    Check(phone.StatusText == "USB ile bağlı" && !phone.IsCharging && phone.BatteryPercent == null,
        "USB bağlantısı doğrulanmamış şarj veya yüzde göstermez");
    Check(new BatteryDeviceInfo { BatteryPercent = 0 }.StatusText == "%0", "Boş pil görünür yüzde olarak sunulur");
    Check(new BatteryDeviceInfo { BatteryPercent = 50, IsCharging = true }.StatusText == "⚡%50",
        "Doğrulanmış şarj yüzdesinin gösterimi korunur");

    const int productId = 1234;
    DateTime now = DateTime.Now;
    string otherProductLog = Path.Combine(directory, "products_5678_mw.log");
    File.WriteAllText(otherProductLog, "{\"level\":99}");
    File.SetLastWriteTime(otherProductLog, now.AddMinutes(-1));
    Check(RazerLogReader.ReadBattery(directory, productId, now) == null,
        "Başka Razer ürününün logu kullanılmaz");

    string matchingLog = Path.Combine(directory, "products_1234_mw.log");
    foreach (var sample in new[] { (Text: "{\"level\":55}\n{\"level\":0}", Expected: (int?)0),
                                  (Text: "{\"level\":100}", Expected: (int?)100),
                                  (Text: "{\"level\":101}", Expected: (int?)null),
                                  (Text: "{\"level\":50}\n{\"level\":-1}", Expected: (int?)null),
                                  (Text: "bozuk günlük", Expected: (int?)null) })
    {
        File.WriteAllText(matchingLog, sample.Text);
        File.SetLastWriteTime(matchingLog, now.AddMinutes(-1));
        Check(RazerLogReader.ReadBattery(directory, productId, now) == sample.Expected,
            $"Razer log değeri doğrulanır: {sample.Text.Replace('\n', ' ')}");
    }
    File.WriteAllText(matchingLog, "{\"level\":80}");
    File.SetLastWriteTime(matchingLog, now.AddMinutes(-31));
    Check(RazerLogReader.ReadBattery(directory, productId, now) == null, "Eski Razer logu kullanılmaz");
    File.SetLastWriteTime(matchingLog, now.AddMinutes(5));
    Check(RazerLogReader.ReadBattery(directory, productId, now) == null, "Gelecek tarihli Razer logu kullanılmaz");
}
finally
{
    CultureInfo.CurrentCulture = originalCulture;
    Directory.Delete(directory, recursive: true);
}
Console.WriteLine($"{checks} regresyon kontrolü başarılı.");

# Taskbar Battery Widget

Windows 10/11 görev çubuğunun sol tarafına yerleşen; **Razer** kablosuz fare/kulaklıkların, **SteelSeries Arctis / Nova** kablosuz kulaklıkların, **AirPods / Bluetooth** cihazların ve USB ile bağlı **iPhone / iPad** cihazların pil seviyelerini gerçek zamanlı gösteren ultra hafif C# (.NET 8 WPF) uygulaması.

## ✨ Özellikler

- **Doğrudan USB HID Okuma (Uygulamasız Çalışır):**
  - **Razer (`VID_1532`):** Razer Synapse kapalı olsa bile doğrudan USB Dongle üzerinden pil yüzdesini (`0x80`) ve şarj durumunu (`0x84`) okur (Synapse 4 log yedeklemeli).
  - **SteelSeries (`VID_1038`):** Arctis 7+, 7P+, 7X+, Nova 5, Nova 7 ve eski Arctis 7/Pro serilerinin pil ve açık/kapalı/şarj durumunu doğrudan USB HID üzerinden okur.
- **Bluetooth & AirPods Desteği:** Windows SetupAPI (`DEVPKEY_Bluetooth_Battery`) üzerinden bağlı Bluetooth kulaklık, fare ve oyun kollarının pil yüzdesini gösterir.
- **iPhone / iPad USB Algılama:** Telefon kablo ile bilgisayara takıldığında şarj durumunu görev çubuğunda gösterir.
- **Görev Çubuğuna Sabit (Docked) & Tam Ekran Duyarlı:**
  - `Shell_TrayWnd` üzerine kenetlenir, `Win + D` yapıldığında kaybolmaz.
  - Tam ekran oyun veya video açıldığında otomatik gizlenir.
  - Bağlı veya açık olmayan cihazlar otomatik gizlenir.
- **Dinamik Renk Uyarıları:**
  - 🟢 **%46 – %100:** Yeşil
  - 🟡 **%21 – %45:** Sarı / Turuncu
  - 🔴 **%0 – %20:** Kırmızı
- **Ultra Düşük Kaynak Tüketimi:** `RenderMode.SoftwareOnly` ve bellek sıkıştırma (`SetProcessWorkingSetSize`) ile ~15-40 MB RAM ve %0 CPU kullanımı.

## 🚀 Derleme ve Çalıştırma

Bilgisayarınızda **.NET 8.0 SDK** kurulu olmalıdır:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
./publish/TaskbarBatteryWidget.exe
```

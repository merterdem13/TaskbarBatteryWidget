# Taskbar Battery Widget

Windows 10/11 görev çubuğunda Razer, SteelSeries ve Windows'un pil bilgisi sunduğu Bluetooth cihazlarını gösteren C# (.NET 8 WPF) uygulaması. USB ile bağlı iPhone/iPad için bağlantı göstergesi de içerir. Cihazlar 15 saniyede bir sorgulanır.

## ✨ Özellikler

- **Doğrudan USB HID Okuma (Uygulamasız Çalışır):**
  - **Razer (`VID_1532`):** Razer Synapse kapalı olsa bile doğrudan USB Dongle üzerinden pil yüzdesini (`0x80`) ve şarj durumunu (`0x84`) okur (Synapse 4 log yedeklemeli).
  - **SteelSeries (`VID_1038`):** Arctis 7+, 7P+, 7X+, Nova 5, Nova 7 ve eski Arctis 7/Pro serilerinin pil ve açık/kapalı/şarj durumunu doğrudan USB HID üzerinden okur.
- **Bluetooth & AirPods Desteği:** Windows SetupAPI (`DEVPKEY_Bluetooth_Battery`) üzerinden bağlı Bluetooth kulaklık, fare ve oyun kollarının pil yüzdesini gösterir. Windows sol/sağ/kutu düğümlerini ayrı sunuyorsa AirPods parçalarını ayrı ayrı gösterir; kulaklıklar bağlı değilken yalnızca kutu görünür, parça bilgisi yoksa genel AirPods yüzdesine geri döner.
- **iPhone / iPad USB Algılama:** Telefon kablo ile bilgisayara takıldığında **USB ile bağlı** gösterilir. Gerçek pil yüzdesi veya şarj durumu okunmaz.
- **Görev Çubuğuna Sabit (Docked) & Tam Ekran Duyarlı:**
  - `Shell_TrayWnd` üzerine kenetlenir, `Win + D` yapıldığında kaybolmaz.
  - Tam ekran oyun veya video açıldığında otomatik gizlenir.
  - Bağlı veya açık olmayan cihazlar otomatik gizlenir.
- **Dinamik Renk Uyarıları:**
  - 🟢 **%46 – %100:** Yeşil
  - 🟡 **%21 – %45:** Sarı / Turuncu
  - 🔴 **%0 – %20:** Kırmızı
- **Kaynak Tüketimi:** Yazılımsal WPF çizimi kullanılır. Mevcut çalışma kümesi budama ve çöp toplama davranışı korunmuştur. `SetProcessWorkingSetSize` bellek sıkıştırma değildir; görünen çalışma kümesini azaltması toplam bellek ihtiyacının aynı ölçüde azaldığı anlamına gelmez. RAM/CPU tüketimi sisteme ve cihaz sayısına bağlıdır; sabit bir değer garanti edilmez.

## Konum ve başlangıç ayarları

Konum kilidini sağ tık menüsünden kaldırıp widget'ı sürükleyebilirsiniz. Kaydedilen konum yeniden açılışta yüklenir; bozuk veya ekran dışında kalan konum için varsayılan yerleşim kullanılır. Eski sürümde bölgesel sayı biçimi nedeniyle belirsiz kaydedilmiş konumlar geri yüklenmeyebilir; yeniden taşıyarak kaydedebilirsiniz.

Yenileme, konum kilidi, konum sıfırlama ve Windows başlangıç tercihi hem widget hem bildirim alanı menüsünden yönetilir. Yeni kurulumda otomatik başlangıç kapalıdır; önceki sürümün oluşturduğu mevcut başlangıç kaydı korunur.

## Uyumluluk sınırları

- Razer log yedeği yalnızca aynı ürün kimliğine ait, son 30 dakika içinde güncellenmiş dosyalardan okunur. Dosya güncelliği, pil kaydının kendi güncelliğini tek başına kanıtlamaz.
- SteelSeries modellerinin protokolü ve pil ölçeği donanım üzerinde ayrıca doğrulanmalıdır. Bu düzeltme mevcut HID protokollerini değiştirmez. HID yanıtındaki sıfırın pil/çevrimdışı anlamı cihaz bazında doğrulanmadığından Razer/eski SteelSeries yolları korunmuştur; Bluetooth ve logdan okunan geçerli `%0` değerleri gösterilir.
- Bluetooth pil ve bağlantı bilgisi Windows'un sunduğu özelliklere bağlıdır. Bağlantı özelliği bulunamayan aygıtlarda bağlantısız cihazları gizleme garantisi yoktur. AirPods sol/sağ kulaklık ve kutu düğümleri Windows tarafından ayrı sunulduğunda parça yüzdeleri ayrı okunur; aksi durumda mevcut genel AirPods yüzdesi gösterilir.
- Cihazlar PID/ad üzerinden tekilleştirildiği için aynı modelden birden fazla fiziksel cihazın ayrıştırılması henüz garanti edilmez.
- Tam ekran tespiti birincil ekranı kullanır. Çoklu monitör ve farklı DPI senaryoları ayrıca test edilmelidir.
- Bilgisayarın kendi bataryasını izleme veya şarj sınırı uygulama özelliği yoktur.

## 🚀 Derleme ve Çalıştırma

Derleme için **.NET 8 SDK veya .NET 8 hedefini destekleyen daha yeni bir SDK** gerekir:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
./publish/TaskbarBatteryWidget.exe
```

Bu yayın biçimi .NET'i içermez. Çalıştırılacak bilgisayarda **.NET 8 Desktop Runtime (x64)** kurulu olmalıdır; yalnızca temel .NET Runtime kurulması WPF için yeterli değildir.

## Regresyon kontrolleri

Ek test paketi gerektirmeyen kontrol programı:

```powershell
dotnet run --project Tests/TaskbarBatteryWidget.RegressionTests.csproj -c Release
```

Kontroller kültürden bağımsız konum kaydını, bozuk kayıtları, bağlantı/şarj metinlerini ve Razer log eşleşmesi/değer aralığı/güncellik sınırlarını kapsar. Donanım, menü etkileşimi veya çoklu ekran testi yerine geçmez.

## Tanılama

Yakalanan okuma ve ayar hataları `%LOCALAPPDATA%\TaskbarBatteryWidget\diagnostics.log` dosyasına yazılır. Dosya yaklaşık 256 KB olduğunda `.old` dosyasına döndürülür; en fazla iki günlük dosyası tutulur. HID hataları ürün grubu bazında izole edilir; bir gruptaki istisna sonraki grupların taranmasını durdurmaz.

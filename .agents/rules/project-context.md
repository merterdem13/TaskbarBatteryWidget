# TaskbarBatteryWidget - Project Context & Architecture

- **Repository:** https://github.com/merterdem13/TaskbarBatteryWidget (Private)
- **Tech Stack:** C# / .NET 8.0 WPF (`net8.0-windows`), `HidSharp` (2.6.4), Win32 SetupAPI & User32 P/Invoke.
- **Publish Command:**
  ```powershell
  dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
  ```
- **AutoStart Registry Key:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TaskbarBatteryWidget` -> points to `C:\Users\Mert\.gemini\antigravity\scratch\TaskbarBatteryWidget\publish\TaskbarBatteryWidget.exe`

## Key Implementation Details
1. **Razer Wireless Protocol (`VID_1532`):**
   - Uses zero-access (`dwDesiredAccess = 0`) `CreateFile` + `HidD_SetFeature` / `HidD_GetFeature` on the 91-byte Feature Report interface (`MI_00`, Report ID `0x00`).
   - Command Class `0x07`, Command ID `0x80` (Battery Level 0-255 -> 0-100%) and `0x84` (Charging status).
   - CRC is XOR of bytes `3..88` stored at `req[89]`. Works even when Razer Synapse is closed!
   - Includes fallback reader for Razer Synapse 4 logs (`%LOCALAPPDATA%\Razer\RazerAppEngine\User Data\Logs\products_*_mw*.log`).
2. **SteelSeries Wireless Protocol (`VID_1038`):**
   - Modern models (`0x220E` Arctis 7+, Nova 5/7, etc.): Writes `[0x00, 0xB0]` to `mi_03` output report. Response `inBuf[2]` = state (`0x01` Offline/Hidden, `0x02` Charging, `0x03` Connected/Discharging), `inBuf[3]` = battery level (0..4 scale or 0..100%).
   - Legacy models (Arctis 7 2019 / Pro): Writes `[0x06, 0x18]`.
3. **Bluetooth & Generic HID (`NativeBluetoothBatteryReader.cs`):**
   - Reads `DEVPKEY_Bluetooth_Battery` (`{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2`) and `DEVPKEY_Bluetooth_Connected` (`{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A}, 11`) via `SetupDiGetDevicePropertyW`.
4. **iPhone / iPad USB (`VID_05AC`):**
   - Detects Apple Mobile Device USB connection and shows charging indicator.
5. **Taskbar Docking & Performance (`MainWindow.xaml.cs`):**
   - Owned by `Shell_TrayWnd` via `SetWindowLongPtr(hwnd, GWL_HWNDPARENT, taskbarHwnd)` so `Win+D` never hides it.
   - Auto-hides when a fullscreen game/video is in foreground or when no devices are active.
   - Position is locked by default (`_isPositionLocked = true`), toggleable via right-click context menu.
   - Uses `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` and `SetProcessWorkingSetSize` to keep RAM usage minimal (~15-40 MB) and CPU at 0%.

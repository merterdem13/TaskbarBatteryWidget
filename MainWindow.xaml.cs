using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace TaskbarBatteryWidget;

public class DeviceViewModel
{
    public string Name { get; set; } = "";
    public Geometry IconGeometry { get; set; } = Geometry.Empty;
    public string DisplayText { get; set; } = "";
    public string TooltipText { get; set; } = "";
    public Brush AccentBrush { get; set; } = Brushes.White;
    public Brush TextBrush { get; set; } = Brushes.White;
    public Visibility BarVisibility { get; set; } = Visibility.Visible;
    public double BarFillHeight { get; set; } = 13.0;
}

public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int GWL_HWNDPARENT = -8;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _taskbarSyncTimer;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _lockTrayItem;
    private readonly System.Windows.Forms.ToolStripMenuItem _autoStartTrayItem;
    private bool _isPolling;
    private bool _customPositionLoaded;
    private bool _isPositionLocked = true;
    private bool _hasActiveDevices;
    private string _lastStateSignature = "";
    private IntPtr _lastTaskbarHwnd = IntPtr.Zero;

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarBatteryWidget",
        "position.txt");

    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "TaskbarBatteryWidget";

    // Frozen Static Vector Geometries for Cross-Template Binding
    private static readonly Geometry MouseGeometry = CreateFrozenGeometry(
        "M12,2 C8.69,2 6,4.69 6,8 L6,16 C6,19.31 8.69,22 12,22 C15.31,22 18,19.31 18,16 L18,8 C18,4.69 15.31,2 12,2 Z M13,6 L13,10 L11,10 L11,6 L13,6 Z");

    private static readonly Geometry HeadsetGeometry = CreateFrozenGeometry(
        "M12,2 C6.48,2 2,6.48 2,12 L2,19 C2,20.66 3.34,22 5,22 L8,22 L8,14 L4,14 L4,12 C4,7.58 7.58,4 12,4 C16.42,4 20,7.58 20,12 L20,14 L16,14 L16,22 L19,22 C20.66,22 22,20.66 22,19 L22,12 C22,6.48 17.52,2 12,2 Z");

    private static readonly Geometry EarbudsGeometry = CreateFrozenGeometry(
        "M7.5,3 C5.01,3 3,5.01 3,7.5 C3,9.62 4.47,11.39 6.45,11.86 L6.45,19.5 C6.45,20.33 7.12,21 7.95,21 C8.78,21 9.45,20.33 9.45,19.5 L9.45,7.5 C9.45,5.01 8.5,3 7.5,3 Z M16.5,3 C15.5,3 14.55,5.01 14.55,7.5 L14.55,19.5 C14.55,20.33 15.22,21 16.05,21 C16.88,21 17.55,20.33 17.55,19.5 L17.55,11.86 C19.53,11.39 21,9.62 21,7.5 C21,5.01 18.99,3 16.5,3 Z");

    private static readonly Geometry PhoneGeometry = CreateFrozenGeometry(
        "M16,1 L8,1 C6.34,1 5,2.34 5,4 L5,20 C5,21.66 6.34,23 8,23 L16,23 C17.66,23 19,21.66 19,20 L19,4 C19,2.34 17.66,1 16,1 Z M13,21 L11,21 L11,20 L13,20 L13,21 Z M17,18 L7,18 L7,4 L17,4 L17,18 Z");

    private static Geometry CreateFrozenGeometry(string pathData)
    {
        var geom = StreamGeometry.Parse(pathData).Clone();
        geom.Freeze();
        return geom;
    }

    public MainWindow()
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        InitializeComponent();

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Pil Göstergesi (Taskbar Battery Widget)",
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true
        };
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("Şimdi Yenile", null, (_, _) => _ = RefreshDevicesAsync());
        _lockTrayItem = new System.Windows.Forms.ToolStripMenuItem("Konumu Sabitle")
        {
            Checked = true,
            CheckOnClick = true
        };
        _lockTrayItem.CheckedChanged += (_, _) => SetPositionLocked(_lockTrayItem.Checked);
        trayMenu.Items.Add(_lockTrayItem);
        trayMenu.Items.Add("Konumu Sıfırla (Sol Alt)", null, (_, _) => ResetToDefaultPosition());
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _autoStartTrayItem = new System.Windows.Forms.ToolStripMenuItem("Windows ile Başlat")
        {
            CheckOnClick = true
        };
        _autoStartTrayItem.Click += (_, _) =>
        {
            SetAutoStart(_autoStartTrayItem.Checked);
            UpdateAutoStartMenuState();
        };
        trayMenu.Items.Add(_autoStartTrayItem);
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = trayMenu;

        UpdateAutoStartMenuState();

        Loaded += MainWindow_Loaded;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _pollTimer.Tick += async (_, _) => await RefreshDevicesAsync();

        _taskbarSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _taskbarSyncTimer.Tick += (_, _) => SyncWithTaskbarState();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        AttachToTaskbar(hwnd);
        if (!TryLoadCustomPosition())
            SnapToTaskbarLeft();

        await RefreshDevicesAsync();
        _pollTimer.Start();
        _taskbarSyncTimer.Start();
    }

    private void AttachToTaskbar(IntPtr hwnd)
    {
        IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd != IntPtr.Zero && taskbarHwnd != _lastTaskbarHwnd)
        {
            // Setting Shell_TrayWnd as owner docks the window z-order directly above the taskbar
            // and prevents Win+D (Show Desktop) from minimizing or hiding it!
            SetWindowLongPtr64(hwnd, GWL_HWNDPARENT, taskbarHwnd);
            _lastTaskbarHwnd = taskbarHwnd;
        }
    }

    private void ResetToDefaultPosition()
    {
        _customPositionLoaded = false;
        SnapToTaskbarLeft();

        try
        {
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Pencere veya ayar işlemi", exception);
        }
    }

    private void SnapToTaskbarLeft()
    {
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        IntPtr taskbarHwnd = FindWindow("Shell_TrayWnd", null);
        if (taskbarHwnd != IntPtr.Zero && GetWindowRect(taskbarHwnd, out RECT tbRect))
        {
            double tbLeft = tbRect.Left / dpiX;
            double tbTop = tbRect.Top / dpiY;
            double tbHeight = (tbRect.Bottom - tbRect.Top) / dpiY;
            double widgetHeight = ActualHeight > 10 ? ActualHeight : 32;

            Left = tbLeft + 10;
            Top = tbTop + (tbHeight - widgetHeight) / 2.0;
            return;
        }

        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen != null)
        {
            double screenHeight = screen.Bounds.Height / dpiY;
            double workHeight = screen.WorkingArea.Height / dpiY;
            double taskbarHeight = screenHeight - workHeight;
            if (taskbarHeight < 36) taskbarHeight = 48;
            double widgetHeight = ActualHeight > 10 ? ActualHeight : 32;

            Left = 10;
            Top = screenHeight - (taskbarHeight / 2.0) - (widgetHeight / 2.0);
        }
    }

    private bool TryLoadCustomPosition()
    {
        try
        {
            if (!File.Exists(ConfigPath) ||
                !WindowPosition.TryParse(File.ReadAllText(ConfigPath), out var position))
                return false;

            Left = position.Left;
            Top = position.Top;
            _customPositionLoaded = true;
            EnsureCustomPositionVisible();
            return true;
        }
        catch (Exception exception)
        {
            _customPositionLoaded = false;
            DiagnosticLog.Write("Kayıtlı konumu yükleme", exception);
            return false;
        }
    }

    private void EnsureCustomPositionVisible()
    {
        if (!_customPositionLoaded) return;

        // WPF coordinates are DIPs; screen bounds and the native window rect are pixels.
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        double width = Math.Max(32, ActualWidth);
        double height = Math.Max(24, ActualHeight);
        var widgetBounds = new Rect(Left * dpiX, Top * dpiY, width * dpiX, height * dpiY);
        bool visible = System.Windows.Forms.Screen.AllScreens.Any(screen =>
        {
            var bounds = screen.Bounds;
            return new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height).Contains(widgetBounds);
        });
        if (!visible)
        {
            _customPositionLoaded = false;
            SnapToTaskbarLeft();
        }
    }

    private void SaveCustomPosition()
    {
        try
        {
            string? dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, new WindowPosition(Left, Top).Serialize());
            _customPositionLoaded = true;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Konumu kaydetme", exception);
        }
    }

    private async Task RefreshDevicesAsync()
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            var devices = await Task.Run(BatteryMonitorService.PollAllDevices);

            if (devices.Count == 0)
            {
                _hasActiveDevices = false;
                _lastStateSignature = "";
                MainBorder.Visibility = Visibility.Collapsed;
                TrimWorkingSet();
                return;
            }

            _hasActiveDevices = true;
            string currentSig = string.Join("|", devices.Select(d => $"{d.Id}:{d.Name}:{d.Type}:{d.BatteryPercent}:{d.IsCharging}:{d.StatusText}"));
            if (currentSig != _lastStateSignature)
            {
                _lastStateSignature = currentSig;
                var viewModels = new List<DeviceViewModel>();
                foreach (var d in devices)
                {
                    var (accent, textBrush) = GetColorBrushes(d.BatteryPercent, d.IsCharging);
                    double fillHeight = d.BatteryPercent.HasValue
                        ? Math.Max(2.0, 13.0 * d.BatteryPercent.Value / 100.0)
                        : 13.0;

                    viewModels.Add(new DeviceViewModel
                    {
                        Name = d.Name,
                        IconGeometry = GetIconForType(d.Type),
                        DisplayText = d.StatusText,
                        TooltipText = $"{d.Name} ({d.StatusText})",
                        AccentBrush = accent,
                        TextBrush = textBrush,
                        BarVisibility = d.BatteryPercent.HasValue ? Visibility.Visible : Visibility.Collapsed,
                        BarFillHeight = fillHeight
                    });
                }

                DevicesItemsControl.ItemsSource = viewModels;
            }

            if (!IsFullscreenAppActive())
            {
                MainBorder.Visibility = Visibility.Visible;
            }

            if (!_customPositionLoaded)
            {
                await Dispatcher.InvokeAsync(SnapToTaskbarLeft, DispatcherPriority.Loaded);
            }

            TrimWorkingSet();
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Cihaz listesini yenileme", exception);
        }
        finally
        {
            _isPolling = false;
        }
    }

    private static (Brush Accent, Brush Text) GetColorBrushes(int? pct, bool isCharging)
    {
        if (!pct.HasValue)
        {
            var cyan = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            cyan.Freeze();
            return (cyan, Brushes.White);
        }

        if (pct.Value <= 20)
        {
            var red = new SolidColorBrush(Color.FromRgb(255, 77, 79));
            red.Freeze();
            return (red, red);
        }

        if (pct.Value <= 45)
        {
            var yellow = new SolidColorBrush(Color.FromRgb(250, 173, 20));
            yellow.Freeze();
            return (yellow, yellow);
        }

        var green = new SolidColorBrush(Color.FromRgb(74, 222, 128));
        green.Freeze();
        var white = new SolidColorBrush(Color.FromRgb(245, 245, 245));
        white.Freeze();
        return (green, white);
    }

    private static Geometry GetIconForType(DeviceType type) => type switch
    {
        DeviceType.Mouse => MouseGeometry,
        DeviceType.Headset => HeadsetGeometry,
        DeviceType.Earbuds => EarbudsGeometry,
        DeviceType.Phone => PhoneGeometry,
        _ => MouseGeometry
    };

    private bool IsFullscreenAppActive()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var sb = new System.Text.StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        string cls = sb.ToString();

        // Ignore desktop and taskbar itself
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow")
            return false;

        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen != null && GetWindowRect(fg, out RECT r))
        {
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (r.Left <= 0 && r.Top <= 0 && w >= screen.Bounds.Width && h >= screen.Bounds.Height)
            {
                return true;
            }
        }
        return false;
    }

    private void SyncWithTaskbarState()
    {
        if (!_hasActiveDevices)
        {
            if (MainBorder.Visibility != Visibility.Collapsed)
                MainBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Re-attach if Explorer restarted
        AttachToTaskbar(hwnd);

        // Hide automatically when a fullscreen game or video covers the taskbar
        if (IsFullscreenAppActive())
        {
            if (MainBorder.Visibility != Visibility.Collapsed)
                MainBorder.Visibility = Visibility.Collapsed;
            return;
        }

        if (MainBorder.Visibility != Visibility.Visible)
            MainBorder.Visibility = Visibility.Visible;

        EnsureCustomPositionVisible();
        if (!_customPositionLoaded)
        {
            SnapToTaskbarLeft();
        }

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only allow dragging if user explicitly unlocked the position from Right-Click menu
        if (!_isPositionLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            SaveCustomPosition();
        }
    }

    private void LockPositionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetPositionLocked(LockPositionMenuItem.IsChecked);
    }

    private void SetPositionLocked(bool locked)
    {
        _isPositionLocked = locked;
        LockPositionMenuItem.IsChecked = locked;
        _lockTrayItem.Checked = locked;
    }

    private async void RefreshMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private void ResetPositionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaultPosition();
    }

    private void UpdateAutoStartMenuState()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
            bool enabled = key?.GetValue(AppRegistryName) != null;
            AutoStartMenuItem.IsChecked = enabled;
            _autoStartTrayItem.Checked = enabled;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Pencere veya ayar işlemi", exception);
        }
    }

    private void AutoStartMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SetAutoStart(AutoStartMenuItem.IsChecked);
        UpdateAutoStartMenuState();
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryRunKey, true);
            if (key == null) return;

            if (enable)
            {
                string exePath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppRegistryName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryName, false);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Pencere veya ayar işlemi", exception);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExitApp();
    }

    private static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Optimized, false);
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(proc.Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write("Pencere veya ayar işlemi", exception);
        }
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}

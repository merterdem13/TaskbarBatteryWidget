using System.Threading;
using System.Windows;

namespace TaskbarBatteryWidget;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "TaskbarBatteryWidget_SingleInstance_Mutex", out bool createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_ownsMutex)
                _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}

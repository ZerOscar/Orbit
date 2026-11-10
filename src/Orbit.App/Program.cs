using Avalonia;
using Orbit.App.Services;

namespace Orbit.App;

internal static class Program
{
    private const string InstanceName = @"Local\Orbit.SingleInstance";

    // STAThread 是必需的：全局钩子与 Shell API 都要求单线程单元。
    [STAThread]
    public static void Main(string[] args)
    {
        // 单实例：两个进程会各装一套全局钩子，按一次快捷键弹出两个菜单。
        using var mutex = new Mutex(true, InstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Log.Write("已有实例在运行，本次启动直接退出");
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 启动阶段崩了，进程会立刻消失、什么都看不到 —— 至少留下一条线索。
            Log.Write(ex, "启动失败");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

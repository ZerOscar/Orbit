using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Orbit.App.Services;

namespace Orbit.App;

public partial class App : Application
{
    private AppHost? _host;

    public override void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()),
                $"未处理异常 (终止={e.IsTerminating})");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Write(e.Exception, "未观察的任务异常");
            e.SetObserved();
        };
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            // 托盘常驻型应用：没有主窗口，不能因为窗口全关就退出。
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.ShutdownRequested += (_, _) => Log.Write("系统请求退出");
                desktop.Exit += (_, _) =>
                {
                    Log.Write("应用退出");
                    _host?.Dispose();
                    _host = null;
                };
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    Log.Write(e.Exception, "UI 线程未处理异常");
                    e.Handled = true;
                };

                _host = new AppHost(desktop);
                _host.Start();
            }
            else
            {
                Log.Write($"非桌面生命周期：{ApplicationLifetime?.GetType().Name ?? "null"}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(ex, "初始化失败");
        }

        base.OnFrameworkInitializationCompleted();
    }
}

// 组装层：配置、全局输入、手势识别、菜单、托盘，各管各的，这里只负责把它们接起来。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Orbit.App.Platform;
using Orbit.App.Settings;
using Orbit.Core.Config;
using System.Diagnostics;

namespace Orbit.App.Services;

public sealed class AppHost : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly WindowsGlobalInput _input = new();
    private OrbitConfig _config;
    private TriggerService? _triggers;
    private RadialController? _radial;
    private TrayIcon? _tray;
    private NativeMenuItem? _pauseItem;
    private SettingsWindow? _settings;

    public AppHost(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _config = ConfigStore.Load();
    }

    public void Start()
    {
        Log.Write($"Orbit 启动 · {Environment.OSVersion.Version} · 启动耗时 {StartupMilliseconds()}ms · 配置 {ConfigStore.DefaultPath}");

        // 第一次运行就把默认配置落盘：用户可以直接打开改，设置页也有确定的目标路径。
        if (!File.Exists(ConfigStore.DefaultPath))
        {
            Persist();
            Log.Write("已写入默认配置");
        }

        _triggers = new TriggerService(_input);
        _radial = new RadialController(_config);

        _triggers.SelectionGesture += (_, e) => _radial.ToggleAt(e.X, e.Y);
        _triggers.DragStarted += (_, e) => _radial.BeginDrag(e.X, e.Y);
        _triggers.PointerMoved += (_, e) => _radial.UpdatePointer(e.X, e.Y);
        _triggers.DragEnded += (_, e) => _radial.CommitDrag(e.X, e.Y);
        _radial.Failed += message => Log.Write($"动作失败：{message}");

        _triggers.Configure(_config.Triggers);
        _triggers.Paused = _config.Paused;
        if (_triggers.Start())
            Log.Write($"全局输入已启动，手势 {_config.Triggers.Count(g => g.Enabled)} 个，动作 {_config.Actions.Count} 个");
        else
            Log.Write($"全局输入启动失败：{_triggers.Error}");

        CreateTray();
        MaybeSnapshot();
        MaybeSettingsSnapshot();

        // 调试用：启动后直接把设置窗口打开，方便脚本对真实窗口做点击/拖拽验证。
        if (Environment.GetEnvironmentVariable("ORBIT_OPEN_SETTINGS") == "1")
            Avalonia.Threading.DispatcherTimer.RunOnce(OpenSettings, TimeSpan.FromMilliseconds(300));
    }

    /// <summary>
    /// 进程创建到主界面就绪的耗时，用来判断发布版冷/热启动是否达标。
    /// </summary>
    private static double StartupMilliseconds()
    {
        try
        {
            return Math.Round((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 打开设置窗口、渲染成 PNG 后退出，用于在没有交互桌面的环境里检查排版。
    /// 设置环境变量 ORBIT_SETTINGS_SNAPSHOT=&lt;输出路径&gt; 启用。
    /// </summary>
    private void MaybeSettingsSnapshot()
    {
        if (Environment.GetEnvironmentVariable("ORBIT_SETTINGS_SNAPSHOT") is not { Length: > 0 } path)
            return;

        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            OpenSettings();
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    if (_settings is { } window)
                    {
                        var width = window.ClientSize.Width > 1 ? window.ClientSize.Width : window.Width;
                        var height = window.ClientSize.Height > 1 ? window.ClientSize.Height : window.Height;
                        var size = new PixelSize(
                            Math.Max(1, (int)Math.Ceiling(width)),
                            Math.Max(1, (int)Math.Ceiling(height)));
                        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
                        bitmap.Render(window);
                        bitmap.Save(path);
                        Log.Write($"设置页渲染已保存：{path} ({size.Width}x{size.Height})");
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Write(ex, "设置页渲染失败");
                }
                _lifetime.Shutdown();
            }, TimeSpan.FromMilliseconds(900));
        }, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// 离屏渲染一张菜单图并退出，用于在没有交互桌面的环境下检查绘制结果。
    /// 设置环境变量 ORBIT_SNAPSHOT=&lt;输出路径&gt; 启用。
    /// </summary>
    private void MaybeSnapshot()
    {
        if (Environment.GetEnvironmentVariable("ORBIT_SNAPSHOT") is not { Length: > 0 } path || _radial is null)
            return;

        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            _radial.ShowAt(300, 300, activate: false);
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                try
                {
                    if (_radial.Window is { } window)
                    {
                        var size = new PixelSize(
                            Math.Max(1, (int)Math.Ceiling(window.Width)),
                            Math.Max(1, (int)Math.Ceiling(window.Height)));
                        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
                        bitmap.Render(window);
                        bitmap.Save(path);
                        Log.Write($"离屏渲染已保存：{path}");
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Log.Write(ex, "离屏渲染失败");
                }
                _lifetime.Shutdown();
            }, TimeSpan.FromMilliseconds(700));
        }, TimeSpan.FromMilliseconds(400));
    }

    private void CreateTray()
    {
        try
        {
            var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Orbit/Assets/orbit.ico")));
            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Orbit · Ctrl+Alt+Space",
                Menu = BuildMenu(),
                IsVisible = true,
            };
            _tray.Clicked += (_, _) => OpenMenuAtCursor();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 托盘起不来也要能用快捷键，所以这里只记日志，不中断。
            Log.Write(ex, "托盘创建失败");
        }
    }

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var open = new NativeMenuItem("打开菜单");
        open.Click += (_, _) => OpenMenuAtCursor();
        menu.Add(open);

        _pauseItem = new NativeMenuItem(_config.Paused ? "恢复手势" : "暂停手势");
        _pauseItem.Click += (_, _) => TogglePause();
        menu.Add(_pauseItem);

        menu.Add(new NativeMenuItemSeparator());

        var settings = new NativeMenuItem("设置…");
        settings.Click += (_, _) => OpenSettings();
        menu.Add(settings);

        var config = new NativeMenuItem("打开配置文件");
        config.Click += (_, _) => OpenConfig();
        menu.Add(config);

        var reload = new NativeMenuItem("重新加载配置");
        reload.Click += (_, _) => Reload();
        menu.Add(reload);

        menu.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("退出");
        quit.Click += (_, _) => _lifetime.Shutdown();
        menu.Add(quit);

        return menu;
    }

    private void OpenMenuAtCursor()
    {
        var (x, y) = _input.CursorPosition;
        _radial?.ToggleAt(x, y);
    }

    /// <summary>
    /// 打开设置窗口。期间把全局手势停掉：设置页里要用鼠标拖拽排序，
    /// 而 Ctrl+Alt 组合键还会把环形菜单弹到设置窗口头上。
    /// </summary>
    private void OpenSettings()
    {
        if (_settings is { } existing)
        {
            existing.Activate();
            return;
        }

        _radial?.Close(animate: false);
        if (_triggers is not null)
            _triggers.Paused = true;

        var window = new SettingsWindow(_config, ConfigStore.DefaultPath);
        window.Applied += ApplySettings;
        window.Closed += (_, _) =>
        {
            _settings = null;
            // 恢复成配置里的暂停状态，而不是无条件打开。
            if (_triggers is not null)
                _triggers.Paused = _config.Paused;
            Log.Write("设置窗口已关闭");
        };

        _settings = window;
        window.Show();
        window.Activate();
        Log.Write("设置窗口已打开");
    }

    /// <summary>设置页点了保存：写盘、重建手势、换掉正在用的配置，全部即时生效。</summary>
    private void ApplySettings(OrbitConfig config)
    {
        _config = config;
        _triggers?.Configure(_config.Triggers);
        if (_triggers is not null)
            _triggers.Paused = _config.Paused;
        _radial?.Apply(_config);

        if (_pauseItem is not null)
            _pauseItem.Header = _config.Paused ? "恢复手势" : "暂停手势";

        Log.Write($"设置已应用：切片 {_config.Actions.Count(a => a.Enabled)} 个，手势 {_config.Triggers.Count(g => g.Enabled)} 个");
    }

    private void TogglePause()
    {
        _config.Paused = !_config.Paused;
        if (_pauseItem is not null)
            _pauseItem.Header = _config.Paused ? "恢复手势" : "暂停手势";
        if (_triggers is not null)
            _triggers.Paused = _config.Paused;
        Persist();
        Log.Write(_config.Paused ? "手势已暂停" : "手势已恢复");
    }

    private void Reload()
    {
        // 设置窗口里编辑的是旧配置的克隆，重新加载后它会和实际配置脱节，先关掉。
        _settings?.Close();
        _settings = null;

        _config = ConfigStore.Load();
        _triggers?.Configure(_config.Triggers);
        _radial?.Apply(_config);
        Log.Write("配置已重新加载");
    }

    private void OpenConfig()
    {
        try
        {
            ConfigStore.Save(_config);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = ConfigStore.DefaultPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(ex, "打开配置失败");
        }
    }

    private void Persist()
    {
        try
        {
            ConfigStore.Save(_config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(ex, "保存配置失败");
        }
    }

    public void Dispose()
    {
        _settings?.Close();
        _settings = null;
        _triggers?.Dispose();
        _radial?.Dispose();
        if (_tray is not null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}

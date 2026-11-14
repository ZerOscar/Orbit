// 通用设置：开机自启、更新检查、版本信息。
//
// 自启的勾选只改「意图」，真正写注册表在保存时做（SettingsWindow.Save），
// 这样点取消不会留下系统改动。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Orbit.App.Services;
using Orbit.Core.Config;

namespace Orbit.App.Settings;

internal sealed class GeneralPanel : Grid
{
    private readonly OrbitConfig _config;
    private readonly UpdateChecker _checker;

    private readonly CheckBox _autoStart;
    private readonly CheckBox _checkUpdates;
    private readonly TextBlock _autoStartNote = SettingsUi.Text("", 11, SettingsUi.TextMuted);
    private readonly TextBlock _status = SettingsUi.Text("", 11.5, SettingsUi.TextMuted);
    private readonly Button _checkButton;
    private readonly StackPanel _statusRow = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Button? _viewButton;

    public GeneralPanel(OrbitConfig config, UpdateChecker checker)
    {
        _config = config;
        _checker = checker;

        RowDefinitions = new RowDefinitions("Auto");

        var stack = new StackPanel { Spacing = 0 };

        // ───── 启动 ─────
        stack.Children.Add(WithTop(SettingsUi.SectionHeader("启动"), 0, 10));

        _autoStart = SettingsUi.Toggle("开机时自动启动 Orbit", _config.StartWithWindows);
        _autoStart.IsEnabled = AutoStart.IsSupported;
        _autoStart.PropertyChanged += (_, e) =>
        {
            if (e.Property != ToggleButton.IsCheckedProperty)
                return;
            _config.StartWithWindows = _autoStart.IsChecked == true;
            RefreshAutoStartNote();
        };
        stack.Children.Add(_autoStart);

        _autoStartNote.TextWrapping = TextWrapping.Wrap;
        _autoStartNote.Margin = new Thickness(26, 2, 8, 0);
        stack.Children.Add(_autoStartNote);

        _checkUpdates = SettingsUi.Toggle("启动时检查新版本", _config.CheckForUpdates);
        _checkUpdates.PropertyChanged += (_, e) =>
        {
            if (e.Property != ToggleButton.IsCheckedProperty)
                return;
            _config.CheckForUpdates = _checkUpdates.IsChecked == true;
        };
        var updatesToggle = WithTop(_checkUpdates, 16, 0);
        stack.Children.Add(updatesToggle);

        var updatesNote = SettingsUi.Text("每天最多检查一次。发现新版本会在右下角提示，不下载、不自动安装。", 11, SettingsUi.TextMuted);
        updatesNote.TextWrapping = TextWrapping.Wrap;
        updatesNote.Margin = new Thickness(26, 2, 8, 0);
        stack.Children.Add(updatesNote);

        // ───── 版本 ─────
        stack.Children.Add(WithTop(SettingsUi.SectionHeader("版本"), 26, 12));

        var versionLabel = SettingsUi.Text($"当前版本 {UpdateChecker.CurrentVersionText}", 12.5, SettingsUi.TextPrimary);

        _checkButton = SettingsUi.Flat("检查更新", 30, 92);
        _checkButton.Click += (_, _) => _ = CheckNowAsync();

        var versionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(versionLabel, 0);
        versionLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_checkButton, 2);
        versionRow.Children.Add(versionLabel);
        versionRow.Children.Add(_checkButton);
        stack.Children.Add(versionRow);

        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        _statusRow.Children.Add(_status);
        stack.Children.Add(_statusRow);

        // ───── 关于 ─────
        stack.Children.Add(WithTop(SettingsUi.SectionHeader("关于"), 26, 12));

        var about = SettingsUi.Text("单文件、免安装、不写系统目录。配置文件就在上面的路径里。", 11.5, SettingsUi.TextMuted);
        about.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(about);

        var repo = SettingsUi.Ghost("打开 GitHub 仓库");
        repo.HorizontalAlignment = HorizontalAlignment.Left;
        // Ghost 自带左右内边距，负 margin 让文字和上面的段落左边缘对齐。
        repo.Margin = new Thickness(-10, 6, 0, 0);
        repo.Click += (_, _) => OpenLink($"https://github.com/{UpdateChecker.Repository}");
        stack.Children.Add(repo);

        Children.Add(stack);

        RefreshAutoStartNote();
        SetStatus("每天自动检查一次，也可以现在手动检查", SettingsUi.TextMuted, null);

        // 调试用：打开设置页就自动点一次「检查更新」，配合 ORBIT_SETTINGS_SNAPSHOT
        // 能把异步结果截进图里，省掉写脚本点按钮。
        if (Environment.GetEnvironmentVariable("ORBIT_AUTO_CHECK") == "1")
            Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = CheckNowAsync(), TimeSpan.FromMilliseconds(100));
    }

    /// <summary>配置被「恢复默认」整体替换后重建界面。</summary>
    public void Rebuild()
    {
        _autoStart.IsChecked = _config.StartWithWindows;
        _checkUpdates.IsChecked = _config.CheckForUpdates;
        RefreshAutoStartNote();
        SetStatus("每天自动检查一次，也可以现在手动检查", SettingsUi.TextMuted, null);
    }

    private void RefreshAutoStartNote()
    {
        if (!AutoStart.IsSupported)
        {
            _autoStartNote.Text = "当前系统不支持（仅 Windows 可用）";
            _autoStartNote.Foreground = SettingsUi.Warning;
            return;
        }

        var registered = AutoStart.IsEnabled();
        if (_config.StartWithWindows && !registered)
        {
            _autoStartNote.Text = "点保存后写入启动项（当前用户，无需管理员权限）";
            _autoStartNote.Foreground = SettingsUi.TextMuted;
        }
        else if (_config.StartWithWindows)
        {
            _autoStartNote.Text = "已登记到当前用户的启动项";
            _autoStartNote.Foreground = SettingsUi.TextMuted;
        }
        else if (registered)
        {
            _autoStartNote.Text = "启动项仍在，点保存后会移除";
            _autoStartNote.Foreground = SettingsUi.Warning;
        }
        else
        {
            _autoStartNote.Text = "从任务管理器「启动」里也能单独关掉";
            _autoStartNote.Foreground = SettingsUi.TextMuted;
        }
    }

    private async Task CheckNowAsync()
    {
        _checkButton.IsEnabled = false;
        SetStatus("正在检查…", SettingsUi.TextMuted, null);

        try
        {
            // force：设置页的手动检查不受 24 小时节流限制。
            var result = await _checker.CheckAsync(force: true);

            switch (result.Status)
            {
                case UpdateStatus.Available when result.Update is { } update:
                    SetStatus($"发现新版本 {update.Tag}（当前 {UpdateChecker.CurrentVersionText}）",
                        SettingsUi.AccentBrush, update.Url);
                    break;
                case UpdateStatus.UpToDate:
                    SetStatus($"已是最新版本 {UpdateChecker.CurrentVersionText}", SettingsUi.TextMuted, null);
                    break;
                default:
                    SetStatus(result.Message, SettingsUi.Danger, null);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus($"检查失败：{ex.Message}", SettingsUi.Danger, null);
        }
        finally
        {
            _checkButton.IsEnabled = true;
        }
    }

    private void SetStatus(string text, IBrush brush, string? link)
    {
        _status.Text = text;
        _status.Foreground = brush;

        if (_viewButton is not null)
        {
            _statusRow.Children.Remove(_viewButton);
            _viewButton = null;
        }

        if (link is null)
            return;

        _viewButton = SettingsUi.Ghost("打开下载页");
        _viewButton.Click += (_, _) => OpenLink(link);
        _statusRow.Children.Add(_viewButton);
    }

    private static void OpenLink(string url)
    {
        try
        {
            if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(ex, "打开下载页失败");
        }
    }

    private static Control WithTop(Control control, double top, double bottom)
    {
        control.Margin = new Thickness(0, top, 0, bottom);
        return control;
    }
}

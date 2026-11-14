// 设置窗口。左边永远是那个环本身——改外观、改顺序都能立刻看到结果；
// 右边按「动作 / 手势」分成两页。
//
// 编辑的是一份克隆，点保存才写回并生效，点取消什么都不变。
// 打开期间全局手势会被暂停，否则在窗口里按快捷键会把菜单弹到设置窗口头上。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Avalonia.Controls.Grid;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Orbit.App.Controls;
using Orbit.App.Services;
using Orbit.Core.Actions;
using Orbit.Core.Config;

namespace Orbit.App.Settings;

internal sealed class SettingsWindow : Window
{
    private readonly OrbitConfig _working;
    private readonly string _configPath;
    private readonly UpdateChecker _checker;

    private readonly DonutMenu _preview = new() { IsHitTestVisible = false };
    private readonly Panel _previewHost = new();
    private readonly TextBlock _previewNote = SettingsUi.Text("", 11, SettingsUi.TextMuted);

    private readonly ActionPanel _actionPanel;
    private readonly GesturePanel _gesturePanel;
    private readonly GeneralPanel _generalPanel;
    private readonly Control[] _panels;
    private readonly Button[] _tabs;
    private readonly TextBlock _status = SettingsUi.Text("", 11.5, SettingsUi.TextMuted);

    private Slider? _radiusSlider;
    private Slider? _holeSlider;
    private Slider? _opacitySlider;
    private Button? _resetButton;
    private int _resetArmed;
    private int _currentTab;

    public SettingsWindow(OrbitConfig config, string configPath, UpdateChecker checker)
    {
        _working = config.Clone();
        _configPath = configPath;
        _checker = checker;

        Title = "Orbit 设置";
        Width = 940;
        Height = 724;
        MinWidth = 880;
        MinHeight = 640;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = SettingsUi.BackgroundBrush;
        ShowInTaskbar = true;
        FontFamily = SettingsUi.UiFont;
        try
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Orbit/Assets/orbit.ico")));
        }
        catch (Exception)
        {
            // 图标缺失不影响使用。
        }

        _actionPanel = new ActionPanel(_working.Actions);
        _gesturePanel = new GesturePanel(_working.Triggers);
        _generalPanel = new GeneralPanel(_working, _checker);

        _panels = [_actionPanel, _gesturePanel, _generalPanel];
        _tabs = [MakeTab("动作", 0), MakeTab("手势", 1), MakeTab("通用", 2)];

        Content = BuildLayout();
        RefreshPreview();
        SwitchTab(0);

        // 截图验证时直接落到指定页。
        if (int.TryParse(Environment.GetEnvironmentVariable("ORBIT_SETTINGS_TAB"), out var startTab))
            SwitchTab(Math.Clamp(startTab, 0, _panels.Length - 1));

        _actionPanel.Changed += (_, _) => RefreshPreview();
        _gesturePanel.Changed += (_, _) => SetStatus("", SettingsUi.TextMuted);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    /// <summary>点保存后触发，参数是已经校验过的新配置。</summary>
    public event Action<OrbitConfig>? Applied;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
    }

    /// <summary>
    /// 高分屏上 724 逻辑像素可能就顶满整个屏幕（150% 下等于 1086 物理像素），
    /// 窗口会跑到屏幕外、按钮点不到。所以按工作区再收一次，并自己居中。
    /// </summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var work = screen.WorkingArea;
        var maxWidth = work.Width / scale - 40;
        var maxHeight = work.Height / scale - 60;

        MinHeight = Math.Min(MinHeight, maxHeight);
        MinWidth = Math.Min(MinWidth, maxWidth);
        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);

        Position = new PixelPoint(
            work.X + Math.Max(0, (int)((work.Width - Width * scale) / 2)),
            work.Y + Math.Max(0, (int)((work.Height - Height * scale) / 2)));
    }

    // ───────────── 布局 ─────────────

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 12), Spacing = 3 };
        header.Children.Add(SettingsUi.Text("Orbit 设置", 19, SettingsUi.TextPrimary, FontWeight.SemiBold));
        // 显示成 %APPDATA%\Orbit\config.json：跨机器一致，也不会把用户名带进截图。
        var shown = _configPath.Replace(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%",
            StringComparison.OrdinalIgnoreCase);
        header.Children.Add(SettingsUi.Text($"配置：{shown}", 11, SettingsUi.TextMuted));
        SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("336,Auto,*"),
            Margin = new Thickness(24, 0, 24, 0),
        };
        var left = BuildLeft();
        SetColumn(left, 0);
        var divider = new Border
        {
            Width = 1,
            Background = SettingsUi.LineBrush,
            Margin = new Thickness(0, 4, 20, 16),
        };
        SetColumn(divider, 1);
        var right = BuildRight();
        SetColumn(right, 2);
        body.Children.Add(left);
        body.Children.Add(divider);
        body.Children.Add(right);
        SetRow(body, 1);
        root.Children.Add(body);

        var footer = BuildFooter();
        SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private Control BuildLeft()
    {
        var stack = new StackPanel { Spacing = 0, Margin = new Thickness(0, 4, 8, 0) };

        _previewHost.Width = DonutMenu.SideFor(_working.OuterRadius);
        _previewHost.Height = DonutMenu.SideFor(_working.OuterRadius);
        _previewHost.Children.Add(_preview);

        var frame = new Border
        {
            Height = 296,
            CornerRadius = new CornerRadius(14),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#1B1B24"), 0),
                    new GradientStop(Color.Parse("#101016"), 1),
                },
            },
            BorderBrush = SettingsUi.LineBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8),
                Child = _previewHost,
            },
        };
        stack.Children.Add(frame);

        _previewNote.Margin = new Thickness(2, 8, 0, 0);
        stack.Children.Add(_previewNote);

        var appearance = SettingsUi.SectionHeader("外观");
        appearance.Margin = new Thickness(0, 18, 0, 12);
        stack.Children.Add(appearance);

        _radiusSlider = SettingsUi.Slider(90, 210, _working.OuterRadius, 1);
        stack.Children.Add(WithMargin(
            SettingsUi.SliderField(_radiusSlider, "环的半径", v => $"{v:0}"), 0, 0, 0, 8));

        _holeSlider = SettingsUi.Slider(0.25, 0.65, _working.HoleRatio, 0.01);
        stack.Children.Add(WithMargin(
            SettingsUi.SliderField(_holeSlider, "中心孔洞", v => $"{v * 100:0}%"), 0, 0, 0, 8));

        _opacitySlider = SettingsUi.Slider(0.5, 1, _working.SliceOpacity, 0.01);
        stack.Children.Add(WithMargin(
            SettingsUi.SliderField(_opacitySlider, "切片浓度", v => $"{v * 100:0}%"), 0, 0, 0, 4));

        foreach (var slider in new[] { _radiusSlider, _holeSlider, _opacitySlider })
        {
            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                    return;
                _working.OuterRadius = Math.Round(_radiusSlider.Value);
                _working.HoleRatio = Math.Round(_holeSlider.Value, 2);
                _working.SliceOpacity = Math.Round(_opacitySlider.Value, 2);
                RefreshPreview();
            };
        }

        return stack;
    }

    private Control BuildRight()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 0) };

        var tabBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 14),
        };
        foreach (var tab in _tabs)
            tabBar.Children.Add(tab);
        SetRow(tabBar, 0);
        grid.Children.Add(tabBar);

        var host = new Grid();
        foreach (var panel in _panels)
            host.Children.Add(panel);
        SetRow(host, 1);
        grid.Children.Add(host);

        return grid;
    }

    private Control BuildFooter()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(24, 14, 24, 18),
        };

        var open = SettingsUi.Ghost("打开配置文件");
        open.Click += (_, _) => RevealConfig();
        Grid.SetColumn(open, 0);
        grid.Children.Add(open);

        _status.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_status, 1);
        grid.Children.Add(_status);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        _resetButton = SettingsUi.Ghost("恢复默认");
        _resetButton.Click += (_, _) => ResetToDefaults();
        actions.Children.Add(_resetButton);

        var cancel = SettingsUi.Flat("取消", 34, 72);
        cancel.Click += (_, _) => Close();
        actions.Children.Add(cancel);

        var save = SettingsUi.Primary("保存", Save, 34, 84);
        save.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Children.Add(save);

        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var wrapper = new StackPanel
        {
            Children = { SettingsUi.Divider(), grid },
        };
        return wrapper;
    }

    private Button MakeTab(string text, int index)
    {
        var button = new Button
        {
            Content = SettingsUi.Text(text, 12.5, SettingsUi.TextSecondary, FontWeight.Medium),
            Height = 30,
            Padding = new Thickness(16, 0, 16, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            FontFamily = SettingsUi.UiFont,
        };
        button.Click += (_, _) => SwitchTab(index);
        return button;
    }

    private void SwitchTab(int index)
    {
        _currentTab = index;
        for (var i = 0; i < _panels.Length; i++)
        {
            _panels[i].IsVisible = i == index;
            var selected = i == index;
            _tabs[i].Background = selected ? SettingsUi.AccentSoftBrush : Brushes.Transparent;
            _tabs[i].BorderBrush = selected ? SettingsUi.AccentBrush : Brushes.Transparent;
            if (_tabs[i].Content is TextBlock label)
                label.Foreground = selected ? SettingsUi.TextPrimary : SettingsUi.TextSecondary;
        }
        SetStatus("", SettingsUi.TextMuted);
        RefreshPreviewNote();
    }

    private static Control WithMargin(Control control, double left, double top, double right, double bottom)
    {
        control.Margin = new Thickness(left, top, right, bottom);
        return control;
    }

    // ───────────── 预览 ─────────────

    private void RefreshPreview()
    {
        var visible = _working.Actions.Where(a => a.Enabled).ToList();
        _preview.Items = visible
            .Select(a => new DonutItem
            {
                Label = a.Label,
                Glyph = string.IsNullOrWhiteSpace(a.Glyph) ? null : a.Glyph,
                Reason = a.Hint,
            })
            .ToList();
        _preview.OuterRadius = _working.OuterRadius;
        _preview.HoleRatio = _working.HoleRatio;
        _preview.SliceOpacity = _working.SliceOpacity;
        _preview.SnapOpen();

        var side = DonutMenu.SideFor(_working.OuterRadius);
        _previewHost.Width = side;
        _previewHost.Height = side;

        RefreshPreviewNote();
    }

    /// <summary>
    /// 预览环下面的说明。文案跟着当前页走——在「通用」页里说「拖动右侧列表」
    /// 会让人以为右边少了东西。
    /// </summary>
    private void RefreshPreviewNote()
    {
        var visible = _working.Actions.Count(a => a.Enabled);
        _previewNote.TextWrapping = TextWrapping.Wrap;

        if (visible == 0)
        {
            _previewNote.Text = "当前没有启用的切片，环是空的";
            _previewNote.Foreground = SettingsUi.Warning;
            return;
        }

        _previewNote.Foreground = SettingsUi.TextMuted;
        _previewNote.Text = _currentTab == 2
            ? $"{visible} 个切片。左侧外观设置对三种触发方式同时生效。"
            : $"{visible} 个切片。第 1 个从正上方开始顺时针排，拖动右侧列表可调顺序。";
    }

    // ───────────── 命令 ─────────────

    private void SetStatus(string text, IBrush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
    }

    private void Save()
    {
        var gestureProblem = _gesturePanel.Validate();
        if (gestureProblem is not null)
        {
            SwitchTab(1);
            SetStatus(gestureProblem, SettingsUi.Warning);
            return;
        }

        if (_working.Actions.Any(a => string.IsNullOrWhiteSpace(a.Label)))
        {
            SwitchTab(0);
            SetStatus("有切片还没填名字", SettingsUi.Warning);
            return;
        }

        if (_working.Actions.Count(a => a.Enabled) == 0)
        {
            SwitchTab(0);
            SetStatus("至少要有一个启用的切片", SettingsUi.Warning);
            return;
        }

        _working.EnsureConsistency();

        // 自启要写注册表，可能被安全软件拦下。失败时停下来让用户看到原因，
        // 而不是先把配置存了、留下一个「说开着其实没开」的状态。
        if (!AutoStart.Sync(_working.StartWithWindows, out var autoStartError))
        {
            SwitchTab(2);
            SetStatus($"开机自启设置失败：{autoStartError}", SettingsUi.Warning);
            return;
        }

        try
        {
            ConfigStore.Save(_working);
            Applied?.Invoke(_working);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"保存失败：{ex.Message}", SettingsUi.Danger);
        }
    }

    private void ResetToDefaults()
    {
        if (_resetButton is null)
            return;

        if (_resetArmed == 0)
        {
            _resetArmed = 1;
            _resetButton.Content = SettingsUi.Text("再点一次确认", 12.5, SettingsUi.Danger);
            SetStatus("恢复默认会丢掉所有自定义切片和手势", SettingsUi.Warning);

            // 3 秒内没有第二次点击就撤销，避免按钮一直停在危险状态。
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (_resetArmed != 1 || _resetButton is null)
                    return;
                _resetArmed = 0;
                _resetButton.Content = SettingsUi.Text("恢复默认", 12.5, SettingsUi.TextSecondary);
                SetStatus("", SettingsUi.TextMuted);
            }, TimeSpan.FromSeconds(3));
            return;
        }

        _resetArmed = 0;
        _resetButton.Content = SettingsUi.Text("恢复默认", 12.5, SettingsUi.TextSecondary);

        // 原地替换内容而不新建集合：面板持有的是同一份 List 引用。
        var fresh = new OrbitConfig();
        _working.Actions.Clear();
        _working.Actions.AddRange(fresh.Actions.Select(a => a.Clone()));
        _working.Triggers.Clear();
        _working.Triggers.AddRange(fresh.Triggers.Select(t => t.Clone()));
        _working.OuterRadius = fresh.OuterRadius;
        _working.HoleRatio = fresh.HoleRatio;
        _working.SliceOpacity = fresh.SliceOpacity;
        _working.StartWithWindows = fresh.StartWithWindows;
        _working.CheckForUpdates = fresh.CheckForUpdates;

        if (_radiusSlider is not null)
            _radiusSlider.Value = _working.OuterRadius;
        if (_holeSlider is not null)
            _holeSlider.Value = _working.HoleRatio;
        if (_opacitySlider is not null)
            _opacitySlider.Value = _working.SliceOpacity;

        _actionPanel.Rebuild();
        _gesturePanel.Rebuild();
        _generalPanel.Rebuild();
        RefreshPreview();
        SetStatus("已恢复为默认配置，点保存后生效", SettingsUi.TextMuted);
    }

    private void RevealConfig()
    {
        try
        {
            // 只打开文件，不把未保存的编辑写进去——那会让「取消」变成假动作。
            if (!File.Exists(_configPath))
                ConfigStore.Save(ConfigStore.Load());

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _configPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus($"打不开：{ex.Message}", SettingsUi.Danger);
        }
    }
}

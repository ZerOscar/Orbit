// 动作面板：上半是环上切片的顺序（可拖拽重排），下半是选中项的详情表单。
// 表单会随「类型」变：启动程序要浏览 exe，切换桌面只给 next/prev，锁屏干脆没有目标。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Orbit.Core.Actions;

namespace Orbit.App.Settings;

internal sealed class ActionPanel : Grid
{
    private static readonly (ActionKind Kind, string Name)[] Kinds =
    [
        (ActionKind.Launch, "启动程序"),
        (ActionKind.OpenFolder, "打开文件夹"),
        (ActionKind.SwitchDesktop, "切换虚拟桌面"),
        (ActionKind.RunCommand, "执行命令"),
        (ActionKind.LockScreen, "锁定屏幕"),
    ];

    private readonly List<OrbitAction> _actions;
    private readonly ReorderList _list = new();
    private readonly StackPanel _form = new() { Spacing = 0 };
    private readonly ScrollViewer _formScroll;
    private readonly TextBlock _hint = SettingsUi.Text("", 11.5, SettingsUi.TextMuted);

    public ActionPanel(List<OrbitAction> actions)
    {
        _actions = actions;

        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*");

        var head = SettingsUi.SectionHeader("切片顺序");
        head.Margin = new Thickness(0, 0, 0, 6);
        SetRow(head, 0);
        Children.Add(head);

        _list.Margin = new Thickness(0, 0, 0, 8);
        SetRow(_list, 1);
        Children.Add(_list);

        var bar = SettingsUi.Row(8);
        bar.Margin = new Thickness(0, 0, 0, 14);
        var add = SettingsUi.Flat("+ 添加动作", 30);
        add.Click += (_, _) => AddAction();
        var remove = SettingsUi.Ghost("删除选中");
        remove.Click += (_, _) => RemoveSelected();
        bar.Children.Add(add);
        bar.Children.Add(remove);
        bar.Children.Add(_hint);
        SetRow(bar, 2);
        Children.Add(bar);

        var detail = SettingsUi.SectionHeader("详情");
        detail.Margin = new Thickness(0, 0, 0, 10);
        SetRow(detail, 3);
        Children.Add(detail);

        _formScroll = new ScrollViewer
        {
            Content = _form,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 4, 0),
        };
        SetRow(_formScroll, 5);
        Children.Add(_formScroll);

        // 列表 + 表单的固定比例：详情比列表更需要空间。
        RowSpacing = 0;

        _list.OrderChanged += (_, _) =>
        {
            _hint.Text = "顺序已调整";
            Raise();
        };
        _list.SelectionChanged += (_, _) => BuildForm();

        Rebuild();
        UpdateHint();
    }

    /// <summary>任何对配置的修改都会触发，外部据此刷新预览。</summary>
    public event EventHandler? Changed;

    private OrbitAction? Current => _list.Selected as OrbitAction;

    public void Rebuild()
    {
        _list.Rebuild(_actions, BuildRow);
        // 可视区只放 4 行，多的靠滚动。行高 40 + 间距 4。
        _list.RowsHeight = Math.Min(Math.Max(_actions.Count, 1), 4) * 44 + 14;
        if (_list.Selected is null && _actions.Count > 0)
            _list.Select(_actions[0]);
        BuildForm();
    }

    private Control BuildRow(OrbitAction action)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            Height = 40,
        };

        var grip = SettingsUi.Text("⠿", 13, SettingsUi.TextMuted);
        grip.Margin = new Thickness(0, 0, 10, 0);
        SetColumn(grip, 0);
        grid.Children.Add(grip);

        var icons = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#14FFFFFF")),
            Child = SettingsUi.Text(
                string.IsNullOrWhiteSpace(action.Glyph) ? "·" : action.Glyph,
                13,
                action.Enabled ? SettingsUi.TextPrimary : SettingsUi.TextMuted),
        };
        icons.Margin = new Thickness(0, 0, 10, 0);
        SetColumn(icons, 1);
        grid.Children.Add(icons);

        var label = SettingsUi.Text(
            action.Label,
            13,
            action.Enabled ? SettingsUi.TextPrimary : SettingsUi.TextSecondary,
            FontWeight.Medium);
        SetColumn(label, 2);
        grid.Children.Add(label);

        var kind = SettingsUi.Text(
            action.Enabled ? KindName(action.Kind) : "已停用",
            11,
            action.Enabled ? SettingsUi.TextMuted : SettingsUi.Warning);
        SetColumn(kind, 3);
        grid.Children.Add(kind);

        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 0, 12, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = SettingsUi.LineBrush,
            Background = SettingsUi.SurfaceBrush,
        };
    }

    private void BuildForm()
    {
        _form.Children.Clear();
        if (Current is not { } action)
        {
            _form.Children.Add(SettingsUi.Text("选中一个切片来编辑它", 12.5, SettingsUi.TextMuted));
            return;
        }

        var name = SettingsUi.Input(action.Label, "显示在环上的名字");
        name.TextChanged += (_, _) =>
        {
            action.Label = name.Text ?? "";
            RefreshRows();
        };

        // 图标很短，和名称挤一行，省一屏高度。
        var glyph = SettingsUi.Input(action.Glyph, "如 ▤", 76);
        glyph.TextChanged += (_, _) =>
        {
            action.Glyph = glyph.Text ?? "";
            RefreshRows();
        };

        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        SetColumn(name, 0);
        glyph.Margin = new Thickness(8, 0, 0, 0);
        SetColumn(glyph, 1);
        nameRow.Children.Add(name);
        nameRow.Children.Add(glyph);
        _form.Children.Add(SettingsUi.Field("名称", nameRow));

        var kind = SettingsUi.Select(170);
        foreach (var (_, text) in Kinds)
            kind.Items.Add(text);
        kind.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == action.Kind));
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0)
                return;
            var next = Kinds[kind.SelectedIndex].Kind;
            if (next == action.Kind)
                return;
            // 换类型时给个合理的默认目标，免得留着一个不匹配的字符串。
            action.Kind = next;
            action.Target = next switch
            {
                ActionKind.SwitchDesktop => "next",
                ActionKind.LockScreen => "",
                _ => action.Target,
            };
            BuildForm();
            RefreshRows();
        };
        _form.Children.Add(SettingsUi.Field("类型", kind));

        if (action.Kind == ActionKind.SwitchDesktop)
        {
            var dir = SettingsUi.Select(170);
            dir.Items.Add("下一个桌面");
            dir.Items.Add("上一个桌面");
            dir.SelectedIndex = string.Equals(action.Target, "prev", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            dir.SelectionChanged += (_, _) =>
            {
                action.Target = dir.SelectedIndex == 1 ? "prev" : "next";
                RefreshRows();
            };
            _form.Children.Add(SettingsUi.Field("方向", dir));
        }
        else if (action.Kind != ActionKind.LockScreen)
        {
            var target = SettingsUi.Input(action.Target, TargetWatermark(action.Kind));
            target.TextChanged += (_, _) => action.Target = target.Text ?? "";
            var browse = SettingsUi.Flat("浏览…", 32, 68);
            browse.IsVisible = action.Kind is ActionKind.Launch or ActionKind.OpenFolder;
            browse.Click += async (_, _) => await BrowseAsync(action, target);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            SetColumn(target, 0);
            browse.Margin = new Thickness(8, 0, 0, 0);
            SetColumn(browse, 1);
            row.Children.Add(target);
            row.Children.Add(browse);
            _form.Children.Add(SettingsUi.Field("目标", row));
        }

        if (action.Kind == ActionKind.Launch)
        {
            var args = SettingsUi.Input(action.Arguments, "可选，如 --new-window");
            args.TextChanged += (_, _) =>
                action.Arguments = string.IsNullOrWhiteSpace(args.Text) ? null : args.Text;
            _form.Children.Add(SettingsUi.Field("参数", args));
        }

        var hint = SettingsUi.Input(action.Hint, "鼠标停在切片上时显示的一句话");
        hint.TextChanged += (_, _) =>
        {
            action.Hint = string.IsNullOrWhiteSpace(hint.Text) ? null : hint.Text;
            RefreshRows();
        };
        _form.Children.Add(SettingsUi.Field("提示", hint));

        var enabled = SettingsUi.Toggle("在环上显示", action.Enabled);
        enabled.IsCheckedChanged += (_, _) =>
        {
            action.Enabled = enabled.IsChecked == true;
            RefreshRows();
        };

        // 删除放这里而不是表单最底部：字段多的时候它会掉到滚动区外，用户以为没有。
        var delete = SettingsUi.Ghost("删除这个切片");
        delete.Foreground = SettingsUi.Danger;
        delete.Click += (_, _) =>
        {
            _actions.Remove(action);
            _list.Select(null);
            Rebuild();
            Raise();
        };

        var footRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        enabled.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(enabled, 0);
        SetColumn(delete, 1);
        footRow.Children.Add(enabled);
        footRow.Children.Add(delete);
        _form.Children.Add(SettingsUi.Field("启用", footRow));
    }

    private async Task BrowseAsync(OrbitAction action, TextBox target)
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is not { } top)
                return;

            if (action.Kind == ActionKind.OpenFolder)
            {
                var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "选择文件夹",
                    AllowMultiple = false,
                });
                if (folders.Count > 0)
                    target.Text = folders[0].Path.LocalPath;
                return;
            }

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择程序",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("程序") { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"] },
                    FilePickerFileTypes.All,
                ],
            });
            if (files.Count > 0)
                target.Text = files[0].Path.LocalPath;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _hint.Text = $"选择失败：{ex.Message}";
        }
    }

    private void AddAction()
    {
        if (_actions.Count >= 12)
        {
            // 12 片时每片只有 30°，文字已经塞不下了，这里设个软上限。
            _hint.Text = "最多 12 个切片";
            return;
        }

        var action = new OrbitAction { Label = "新动作", Glyph = "＋", Kind = ActionKind.Launch, Target = "" };
        _actions.Add(action);
        Rebuild();
        _list.Select(action);
        Raise();
    }

    private void RemoveSelected()
    {
        if (Current is not { } action)
            return;
        _actions.Remove(action);
        Rebuild();
        Raise();
    }

    private void RefreshRows()
    {
        var keep = _list.Selected;
        _list.Rebuild(_actions, BuildRow);
        if (keep is not null)
            _list.Select(keep);
        Raise();
    }

    private void Raise()
    {
        UpdateHint();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHint()
    {
        var enabled = _actions.Count(a => a.Enabled);
        _hint.Text = _actions.Count == enabled
            ? $"共 {_actions.Count} 个切片"
            : $"共 {_actions.Count} 个切片，{_actions.Count - enabled} 个已停用";
        _hint.Foreground = SettingsUi.TextMuted;
    }

    private static string KindName(ActionKind kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind).Name ?? kind.ToString();

    private static string TargetWatermark(ActionKind kind) => kind switch
    {
        ActionKind.Launch => "explorer.exe 或 ms-settings:",
        ActionKind.OpenFolder => @"C:\Users\你\Downloads",
        ActionKind.RunCommand => "如 shutdown /s /t 60",
        _ => "",
    };
}

// 手势面板：怎么把菜单叫出来。三种方式（拖拽 / 长按 / 组合键）共用一套修饰键设置，
// 换方式时只显示相关字段，避免给用户看一堆用不上的选项。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using static Avalonia.Controls.Grid;
using Avalonia.Media;
using Orbit.Core.Input;

namespace Orbit.App.Settings;

internal sealed class GesturePanel : Grid
{
    private static readonly (TriggerKind Kind, string Name, string Note)[] Kinds =
    [
        (TriggerKind.Drag, "按住并拖动", "最不打扰：手已经在拖东西了，顺手甩出去就出来"),
        (TriggerKind.Hold, "长按不动", "按住不放一小会儿，适合触控板"),
        (TriggerKind.KeyChord, "快捷键", "最可靠，和鼠标无关"),
    ];

    /// <summary>快捷键可选的键。只列常用键，冷门键可以改配置文件。</summary>
    private static readonly string[] KeyChoices =
    [
        "Space", "Enter", "Tab", "Escape", "Back",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "Left", "Right", "Up", "Down", "Home", "End", "PageUp", "PageDown", "Insert", "Delete",
        "OemTilde", "OemMinus", "OemPlus", "OemComma", "OemPeriod", "OemQuestion",
    ];

    private readonly List<TriggerGesture> _gestures;
    private readonly ReorderList _list = new();
    private readonly StackPanel _form = new();
    private readonly TextBlock _warn = SettingsUi.Text("", 11.5, SettingsUi.Warning);

    public GesturePanel(List<TriggerGesture> gestures)
    {
        _gestures = gestures;
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*");

        var head = SettingsUi.SectionHeader("触发方式");
        head.Margin = new Thickness(0, 0, 0, 6);
        SetRow(head, 0);
        Children.Add(head);

        _list.Margin = new Thickness(0, 0, 0, 10);
        SetRow(_list, 1);
        Children.Add(_list);

        var bar = SettingsUi.Row(8);
        bar.Margin = new Thickness(0, 0, 0, 14);
        var add = SettingsUi.Flat("+ 添加手势", 30);
        add.Click += (_, _) => AddGesture();
        bar.Children.Add(add);
        var remove = SettingsUi.Ghost("删除选中");
        remove.Click += (_, _) => RemoveSelected();
        bar.Children.Add(remove);
        SetRow(bar, 2);
        Children.Add(bar);

        var detail = new StackPanel { Spacing = 0 };
        var header = SettingsUi.SectionHeader("详情");
        header.Margin = new Thickness(0, 0, 0, 10);
        detail.Children.Add(header);
        detail.Children.Add(_warn);
        detail.Children.Add(_form);

        var scroll = new ScrollViewer
        {
            Content = detail,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 4, 0),
        };
        SetRow(scroll, 4);
        Children.Add(scroll);

        _list.OrderChanged += (_, _) => Raise();
        _list.SelectionChanged += (_, _) => BuildForm();

        Rebuild();
    }

    public event EventHandler? Changed;

    private TriggerGesture? Current => _list.Selected as TriggerGesture;

    /// <summary>有没有配了但不合法的条目，保存前要拦一下。</summary>
    public string? Validate()
    {
        var usable = _gestures.Where(g => g.Enabled).ToList();
        if (usable.Count == 0)
            return "至少要留一个启用的手势，否则菜单叫不出来";

        for (var i = 0; i < usable.Count; i++)
        {
            if (ValidateGesture(usable[i]) is { } problem)
                return problem;
        }

        return null;
    }

    public void Rebuild()
    {
        _list.Rebuild(_gestures, BuildRow);
        _list.RowsHeight = Math.Min(Math.Max(_gestures.Count, 1), 4) * 44 + 14;
        if (_list.Selected is null && _gestures.Count > 0)
            _list.Select(_gestures[0]);
        BuildForm();
    }

    private Control BuildRow(TriggerGesture gesture)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 40 };

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = gesture.Enabled ? SettingsUi.AccentBrush : SettingsUi.TextMuted,
            Margin = new Thickness(2, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetColumn(dot, 0);
        grid.Children.Add(dot);

        var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(SettingsUi.Text(KeyText(gesture), 13,
            gesture.Enabled ? SettingsUi.TextPrimary : SettingsUi.TextSecondary, FontWeight.Medium));
        stack.Children.Add(SettingsUi.Text(KindName(gesture.Kind), 10.5, SettingsUi.TextMuted));
        SetColumn(stack, 1);
        grid.Children.Add(stack);

        var state = SettingsUi.Text(gesture.Enabled ? "" : "已停用", 11, SettingsUi.Warning);
        SetColumn(state, 2);
        grid.Children.Add(state);

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
        _warn.Text = "";
        if (Current is not { } gesture)
        {
            _form.Children.Add(SettingsUi.Text("选中一个手势来编辑它", 12.5, SettingsUi.TextMuted));
            return;
        }

        var enabled = SettingsUi.Toggle("启用这个手势", gesture.Enabled);
        enabled.IsCheckedChanged += (_, _) =>
        {
            gesture.Enabled = enabled.IsChecked == true;
            RefreshRows();
        };
        _form.Children.Add(SettingsUi.Field("启用", enabled));

        var kind = SettingsUi.Select(170);
        foreach (var (_, name, _) in Kinds)
            kind.Items.Add(name);
        kind.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == gesture.Kind));
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedIndex < 0)
                return;
            var next = Kinds[kind.SelectedIndex].Kind;
            if (next == gesture.Kind)
                return;
            gesture.Kind = next;
            if (next == TriggerKind.KeyChord)
            {
                gesture.Key ??= "Space";
                gesture.Button = TriggerMouseButton.None;
            }
            else
            {
                if (gesture.Button == TriggerMouseButton.None)
                    gesture.Button = TriggerMouseButton.Left;
                gesture.Key = null;
            }
            BuildForm();
            RefreshRows();
        };
        // 说明跟着下拉框走，而不是漂在下一行字段旁边——否则会被读成修饰键的说明。
        var note = SettingsUi.Text(Kinds[Math.Max(0, kind.SelectedIndex)].Note, 11, SettingsUi.TextMuted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 5, 0, 0);

        var kindField = new StackPanel { Spacing = 0 };
        kindField.Children.Add(kind);
        kindField.Children.Add(note);
        _form.Children.Add(SettingsUi.Field("方式", kindField));

        _form.Children.Add(SettingsUi.Field("修饰键", BuildModifiers(gesture)));

        if (gesture.Kind == TriggerKind.KeyChord)
        {
            var key = SettingsUi.Select(120);
            foreach (var name in KeyChoices)
                key.Items.Add(name);
            if (string.IsNullOrWhiteSpace(gesture.Key))
                gesture.Key = "Space";
            key.SelectedIndex = Math.Max(0, Array.IndexOf(KeyChoices, gesture.Key));
            key.SelectionChanged += (_, _) =>
            {
                if (key.SelectedIndex >= 0)
                    gesture.Key = KeyChoices[key.SelectedIndex];
                RefreshRows();
            };
            _form.Children.Add(SettingsUi.Field("按键", key));
        }
        else
        {
            var button = SettingsUi.Select(120);
            button.Items.Add("左键");
            button.Items.Add("右键");
            button.Items.Add("中键");
            button.SelectedIndex = gesture.Button switch
            {
                TriggerMouseButton.Right => 1,
                TriggerMouseButton.Middle => 2,
                _ => 0,
            };
            button.SelectionChanged += (_, _) =>
            {
                gesture.Button = button.SelectedIndex switch
                {
                    1 => TriggerMouseButton.Right,
                    2 => TriggerMouseButton.Middle,
                    _ => TriggerMouseButton.Left,
                };
                RefreshRows();
            };
            _form.Children.Add(SettingsUi.Field("鼠标键", button));

            if (gesture.Kind == TriggerKind.Drag)
            {
                var threshold = SettingsUi.Slider(0, 60, gesture.DragThreshold, 1);
                _form.Children.Add(SettingsUi.Field("灵敏度", SettingsUi.SliderField(
                    threshold, "拖动距离", v => v < 1 ? "系统默认" : $"{v:0} px"), 76));
                threshold.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                        gesture.DragThreshold = (int)threshold.Value;
                };
            }
            else
            {
                var hold = SettingsUi.Slider(200, 1500, gesture.HoldMilliseconds, 50);
                _form.Children.Add(SettingsUi.Field("时长", SettingsUi.SliderField(
                    hold, "按住", v => $"{v:0} ms"), 76));
                hold.PropertyChanged += (_, e) =>
                {
                    if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty)
                        gesture.HoldMilliseconds = (int)hold.Value;
                };
            }
        }

        var delete = SettingsUi.Ghost("删除这个手势");
        delete.Foreground = SettingsUi.Danger;
        delete.HorizontalAlignment = HorizontalAlignment.Left;
        delete.Margin = new Thickness(0, 8, 0, 0);
        delete.Click += (_, _) =>
        {
            _gestures.Remove(gesture);
            _list.Select(null);
            Rebuild();
            Raise();
        };
        _form.Children.Add(delete);

        // 顺手把当前这条的问题说清楚，不用等到点保存。
        if (gesture.Enabled)
        {
            var problem = ValidateGesture(gesture);
            _warn.Text = problem ?? "";
        }
    }

    private Control BuildModifiers(TriggerGesture gesture)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };

        foreach (var (flag, name) in new[]
                 {
                     (ModifierKeys.Ctrl, "Ctrl"),
                     (ModifierKeys.Alt, "Alt"),
                     (ModifierKeys.Shift, "Shift"),
                     (ModifierKeys.Meta, "Win"),
                 })
        {
            var box = SettingsUi.Toggle(name, gesture.Modifiers.HasFlag(flag));
            var captured = flag;
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true)
                    gesture.Modifiers |= captured;
                else
                    gesture.Modifiers &= ~captured;

                _warn.Text = gesture.Enabled ? ValidateGesture(gesture) ?? "" : "";
                RefreshRows(rebuildForm: false);
            };
            panel.Children.Add(box);
        }

        return panel;
    }

    private static string? ValidateGesture(TriggerGesture g)
    {
        if (g.Modifiers == ModifierKeys.None)
            return "至少要勾一个修饰键，否则会和正常操作冲突";
        if (g.Kind == TriggerKind.KeyChord && string.IsNullOrWhiteSpace(g.Key))
            return "还没选按键";
        if (g.Kind != TriggerKind.KeyChord && g.Button == TriggerMouseButton.None)
            return "还没选鼠标键";
        return null;
    }

    private void AddGesture()
    {
        if (_gestures.Count >= 4)
            return;

        var gesture = new TriggerGesture
        {
            Kind = TriggerKind.KeyChord,
            Modifiers = ModifierKeys.Ctrl | ModifierKeys.Shift,
            Key = "Space",
            Button = TriggerMouseButton.None,
        };
        _gestures.Add(gesture);
        Rebuild();
        _list.Select(gesture);
        Raise();
    }

    private void RemoveSelected()
    {
        if (Current is not { } gesture)
            return;
        _gestures.Remove(gesture);
        Rebuild();
        Raise();
    }

    private void RefreshRows(bool rebuildForm = false)
    {
        var keep = _list.Selected;
        _list.Rebuild(_gestures, BuildRow);
        if (keep is not null)
            _list.Select(keep);
        if (rebuildForm)
            BuildForm();
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private static string KindName(TriggerKind kind) =>
        Kinds.FirstOrDefault(k => k.Kind == kind).Name ?? kind.ToString();

    /// <summary>把一条手势写成一句人能读的话。</summary>
    public static string KeyText(TriggerGesture g)
    {
        var mods = VirtualKeys.Describe(g.Modifiers);
        if (string.IsNullOrEmpty(mods))
            mods = "（无修饰键）";

        return g.Kind switch
        {
            TriggerKind.KeyChord => $"{mods} + {g.Key ?? "?"}",
            TriggerKind.Hold => $"{mods} + 长按{ButtonName(g.Button)}",
            _ => $"{mods} + 按住{ButtonName(g.Button)}拖动",
        };
    }

    private static string ButtonName(TriggerMouseButton button) => button switch
    {
        TriggerMouseButton.Left => "左键",
        TriggerMouseButton.Right => "右键",
        TriggerMouseButton.Middle => "中键",
        _ => "鼠标",
    };
}

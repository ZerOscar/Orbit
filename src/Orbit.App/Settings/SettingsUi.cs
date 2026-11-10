// 设置页的视觉基础：配色、控件工厂、少量排版助手。
//
// 之所以全用代码构建而不写 XAML：设置页的内容几乎全是运行时生成的
// （动作列表、手势列表、随类型变化的详情表单），用 XAML 反而要在
// 骨架和动态之间来回跳，还得处理编译期绑定；这里一次性把排版写清楚。

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static Avalonia.Controls.Grid;

namespace Orbit.App.Settings;

internal static class SettingsUi
{
    // 和环形菜单同一套色，设置页看起来才像同一个产品。
    public static readonly Color Background = Color.Parse("#14141A");
    public static readonly Color Surface = Color.Parse("#1E1E27");
    public static readonly Color SurfaceHover = Color.Parse("#262633");
    public static readonly Color SurfaceActive = Color.Parse("#2E2E3D");
    public static readonly Color Accent = Color.Parse("#7C6CF0");
    public static readonly Color AccentSoft = Color.FromArgb(0x3D, 0x7C, 0x6C, 0xF0);
    public static readonly Color Line = Color.Parse("#26FFFFFF");

    public static readonly IBrush BackgroundBrush = new SolidColorBrush(Background);
    public static readonly IBrush SurfaceBrush = new SolidColorBrush(Surface);
    public static readonly IBrush SurfaceHoverBrush = new SolidColorBrush(SurfaceHover);
    public static readonly IBrush SurfaceActiveBrush = new SolidColorBrush(SurfaceActive);
    public static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
    public static readonly IBrush AccentSoftBrush = new SolidColorBrush(AccentSoft);
    public static readonly IBrush LineBrush = new SolidColorBrush(Line);

    public static readonly IBrush TextPrimary = new SolidColorBrush(Color.Parse("#F2F2F7"));
    public static readonly IBrush TextSecondary = new SolidColorBrush(Color.Parse("#9A9AAC"));
    public static readonly IBrush TextMuted = new SolidColorBrush(Color.Parse("#6E6E80"));
    public static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#F07178"));
    public static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#E8B75C"));

    public static FontFamily UiFont { get; } = PickFont();

    /// <summary>优先用能同时显示中英文的无衬线字体，找不到就退回默认。</summary>
    private static FontFamily PickFont()
    {
        string[] candidates =
        [
            "Microsoft YaHei UI", "微软雅黑", "PingFang SC", "Segoe UI Variable Text", "Segoe UI", "Inter",
        ];
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in candidates)
        {
            if (installed.Contains(name))
                return new FontFamily(name);
        }
        return FontFamily.Default;
    }

    public static TextBlock Text(string text, double size = 13, IBrush? brush = null, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = UiFont,
            Foreground = brush ?? TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (weight is { } w)
            block.FontWeight = w;
        return block;
    }

    /// <summary>字段区的标题，小一号、次要色。</summary>
    public static TextBlock Caption(string text) =>
        Text(text, 11.5, TextMuted, FontWeight.Medium);

    public static TextBlock SectionTitle(string text) => Text(text, 12, TextSecondary, FontWeight.SemiBold);

    public static Border Divider() => new()
    {
        Height = 1,
        Background = LineBrush,
        Margin = new Thickness(0, 4),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>小节标题 + 一条横线，用来分隔大块区域。</summary>
    public static Control SectionHeader(string title)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var label = SectionTitle(title);
        Grid.SetColumn(label, 0);
        var line = new Border
        {
            Height = 1,
            Background = LineBrush,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(line, 1);
        grid.Children.Add(label);
        grid.Children.Add(line);
        return grid;
    }

    // ───────────── 输入控件 ─────────────

    public static TextBox Input(string? text = null, string? watermark = null, double width = double.NaN) => new()
    {
        Text = text ?? "",
        Watermark = watermark,
        Width = width,
        FontSize = 13,
        FontFamily = UiFont,
        MinHeight = 32,
        Padding = new Thickness(9, 5, 9, 5),
        Background = new SolidColorBrush(Color.Parse("#17171E")),
        BorderBrush = LineBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public static ComboBox Select(double width = double.NaN) => new()
    {
        Width = width,
        FontSize = 13,
        FontFamily = UiFont,
        MinHeight = 32,
        Padding = new Thickness(9, 4, 4, 4),
        Background = new SolidColorBrush(Color.Parse("#17171E")),
        BorderBrush = LineBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(7),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    public static Button Flat(string text, double height = 32, double width = double.NaN) 
    {
        var button = new Button
        {
            Content = Text(text, 12.5, TextPrimary, FontWeight.Medium),
            Height = height,
            Width = width,
            Padding = new Thickness(14, 0, 14, 0),
            Background = SurfaceActiveBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = UiFont,
        };
        return button;
    }

    /// <summary>
    /// 主按钮自绘。用 Button 的话 Fluent 主题的 :pointerover /template/ 规则会直接改
    /// 模板内层的背景，把强调色盖成一团灰——本地设置的 Background 优先级赢不了它。
    /// </summary>
    public static Control Primary(string text, Action onClick, double height = 34, double width = double.NaN)
    {
        var normal = new SolidColorBrush(Color.Parse("#7C6CF0"));
        var hover = new SolidColorBrush(Color.Parse("#8D7EF7"));
        var pressed = new SolidColorBrush(Color.Parse("#6B5CDC"));

        var label = Text(text, 13, Brushes.White, FontWeight.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;

        var border = new Border
        {
            Height = height,
            Width = width,
            CornerRadius = new CornerRadius(7),
            Background = normal,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#9C8DFF")),
            Padding = new Thickness(16, 0, 16, 0),
            Child = label,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        border.PointerEntered += (_, _) => border.Background = hover;
        border.PointerExited += (_, _) => border.Background = normal;
        border.PointerPressed += (_, e) =>
        {
            border.Background = pressed;
            e.Pointer.Capture(border);
        };
        border.PointerReleased += (_, e) =>
        {
            e.Pointer.Capture(null);
            border.Background = hover;
            var point = e.GetPosition(border);
            if (point.X >= 0 && point.Y >= 0 && point.X <= border.Bounds.Width && point.Y <= border.Bounds.Height)
                onClick();
        };
        return border;
    }

    public static Button Ghost(string text) 
    {
        var button = new Button
        {
            Content = Text(text, 12.5, TextSecondary),
            Height = 30,
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            FontFamily = UiFont,
        };
        return button;
    }

    public static CheckBox Toggle(string text, bool isChecked)
    {
        var box = new CheckBox
        {
            Content = Text(text, 12.5, TextSecondary),
            IsChecked = isChecked,
            FontFamily = UiFont,
            MinHeight = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        return box;
    }

    /// <summary>
    /// 一行「标题 + 控件」，标题列宽固定，保证多行表单左对齐。
    /// </summary>
    public static Grid Field(string label, Control control, double labelWidth = 76)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*"),
            Margin = new Thickness(0, 0, 0, 7),
        };
        var caption = Caption(label);
        caption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(control, 1);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        grid.Children.Add(caption);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>把多个控件横排，中间留固定间距。</summary>
    public static StackPanel Row(double spacing = 8, params Control[] children)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = spacing,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    public static Slider Slider(double min, double max, double value, double step = 1)
    {
        return new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            SmallChange = step,
            LargeChange = step * 5,
            TickFrequency = step,
            IsSnapToTickEnabled = step > 0,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = AccentBrush,
        };
    }

    /// <summary>「标题 + 滑块 + 实时数值」。数值列固定宽度，拖动时不会把布局推来推去。</summary>
    public static Control SliderField(Slider slider, string label, Func<double, string> format)
    {
        var value = Text(format(slider.Value), 11.5, TextSecondary);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.Width = 52;
        value.TextAlignment = TextAlignment.Right;

        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                value.Text = format(slider.Value);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var caption = Caption(label);
        caption.Width = 62;
        SetColumn(caption, 0);
        slider.Margin = new Thickness(8, 0, 8, 0);
        SetColumn(slider, 1);
        SetColumn(value, 2);
        grid.Children.Add(caption);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return grid;
    }
}

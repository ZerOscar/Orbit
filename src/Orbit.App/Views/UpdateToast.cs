// 新版本提示：右下角一张小卡片，8 秒后自己淡出。
//
// 刻意做得不打扰：不抢焦点（ShowActivated=false）、不进任务栏、不拦鼠标点击别处。
// 点是打开 Release 页，点 × 是关掉；两者都算「已提醒」，同一个版本不会再弹。

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Orbit.App.Services;
using Orbit.App.Settings;

namespace Orbit.App.Views;

internal sealed class UpdateToast : Window
{
    private const double CardWidth = 348;
    private const double CardHeight = 94;

    private readonly UpdateInfo _update;
    private readonly UpdateChecker _checker;
    private readonly DispatcherTimer _timer;
    private bool _closing;

    public UpdateToast(UpdateInfo update, UpdateChecker checker)
    {
        _update = update;
        _checker = checker;

        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        // 关键：弹出时不抢焦点，否则会把用户正在输入的东西打断。
        ShowActivated = false;
        Width = CardWidth;
        Height = CardHeight;
        FontFamily = SettingsUi.UiFont;

        Content = BuildCard();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) => Dismiss();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PlaceBottomRight();
        _timer.Start();
        FadeIn();
    }

    private Control BuildCard()
    {
        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = SettingsUi.AccentSoftBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "↑",
                FontSize = 19,
                FontFamily = SettingsUi.UiFont,
                Foreground = SettingsUi.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var title = SettingsUi.Text($"Orbit 有新版本 {_update.Tag}", 13, SettingsUi.TextPrimary, FontWeight.SemiBold);

        var subtitleText = _update.Published is { } published
            ? $"发布于 {published.ToLocalTime():M月d日} · 点击查看"
            : "点击查看更新内容";
        var subtitle = SettingsUi.Text(subtitleText, 11.5, SettingsUi.TextMuted);

        var texts = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Children = { title, subtitle },
        };

        var close = new TextBlock
        {
            Text = "×",
            FontSize = 16,
            FontFamily = SettingsUi.UiFont,
            Foreground = SettingsUi.TextMuted,
            Width = 22,
            Height = 22,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        close.PointerEntered += (_, _) => close.Foreground = SettingsUi.TextPrimary;
        close.PointerExited += (_, _) => close.Foreground = SettingsUi.TextMuted;
        close.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            Dismiss();
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(16, 0, 12, 0),
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(close, 2);
        row.Children.Add(icon);
        row.Children.Add(texts);
        row.Children.Add(close);

        var card = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.Parse("#1E1E27")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A3A4C")),
            BorderThickness = new Thickness(1),
            Child = row,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        card.PointerEntered += (_, _) => card.BorderBrush = SettingsUi.AccentBrush;
        card.PointerExited += (_, _) => card.BorderBrush = new SolidColorBrush(Color.Parse("#3A3A4C"));
        card.PointerReleased += (_, _) => OpenRelease();

        return new Panel { Children = { card } };
    }

    /// <summary>贴右下角。按主屏工作区算，避开任务栏。</summary>
    private void PlaceBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
            return;

        var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        var work = screen.WorkingArea;
        var width = (int)(CardWidth * scale);
        var height = (int)(CardHeight * scale);

        Position = new PixelPoint(
            work.X + Math.Max(0, work.Width - width - (int)(20 * scale)),
            work.Y + Math.Max(0, work.Height - height - (int)(20 * scale)));
    }

    private void FadeIn()
    {
        _ = FadeAsync(0, 1, 180);
    }

    private void OpenRelease()
    {
        Dismiss();
        try
        {
            // 只信 https 的 GitHub 链接，别的一律不打开——避免服务端返回内容被当成跳板。
            if (!_update.Url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return;

            Process.Start(new ProcessStartInfo { FileName = _update.Url, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(ex, "打开 Release 页失败");
        }
    }

    private void Dismiss()
    {
        if (_closing)
            return;

        _closing = true;
        _timer.Stop();
        _checker.MarkNotified(_update);
        _ = FadeAsync(Opacity, 0, 160).ContinueWith(_ => Dispatcher.UIThread.Post(Close));
    }

    /// <summary>
    /// 手写补间而不走 Animation：这里只需要透明度，用动画系统反而要处理
    /// KeyFrame 的 animator 类型，出错时是静默的。
    /// </summary>
    private async Task FadeAsync(double from, double to, int milliseconds)
    {
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            if (_closing && to > from)
                return;
            Opacity = from + (to - from) * i / steps;
            try
            {
                await Task.Delay(milliseconds / steps);
            }
            catch (Exception)
            {
                return;
            }
        }
        Opacity = to;
    }
}

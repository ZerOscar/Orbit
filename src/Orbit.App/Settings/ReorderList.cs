// 可点击选中、可拖拽重排的列表。
//
// 没用 ListBox：它的选中由内置 SelectionModel 管，重排要额外同步一份顺序，
// 而且拖拽排序得接平台的 DragDrop，中途丢事件很难查。这里直接管一个
// StackPanel 的行，顺序就是 Children 的顺序，重排时 Move 一次，
// 视觉和模型在同一帧里对齐。

using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Orbit.App.Services;

namespace Orbit.App.Settings;

internal sealed class ReorderList : Border
{
    /// <summary>超过这个位移才算拖拽，否则当成点击。</summary>
    private const double DragSlop = 6;

    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty;

    private IList? _items;
    private readonly List<Control> _controls = [];

    private Control? _pressed;
    private double _pressY;
    private int _dragIndex = -1;
    private bool _dragging;
    private object? _selected;

    public ReorderList()
    {
        _empty = SettingsUi.Text("还没有条目", 12, SettingsUi.TextMuted);
        _empty.Margin = new Thickness(4, 10, 0, 0);
        _empty.IsVisible = false;

        _scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 2, 0),
        };
        Child = _scroll;
        Padding = new Thickness(0);
        Background = Brushes.Transparent;

        PointerMoved += OnListMoved;
        PointerReleased += OnListReleased;
        PointerCaptureLost += OnListCaptureLost;
    }

    /// <summary>行的顺序被拖动改变了。</summary>
    public event EventHandler? OrderChanged;

    /// <summary>选中的条目变了（拖拽重排不算）。</summary>
    public event EventHandler? SelectionChanged;

    public object? Selected => _selected;

    /// <summary>列表的可视高度。固定住，超出的行才会进入滚动而不是继续撑高面板。</summary>
    public double RowsHeight
    {
        set => Height = value;
    }

    /// <summary>绑定一组条目并生成行。items 是同一份被编辑的集合，重排会直接改它。</summary>
    public void Bind<T>(IList<T> items, Func<T, Control> factory) where T : class
    {
        _items = (IList)items;
        _rows.Children.Clear();
        _controls.Clear();

        foreach (var item in items)
            Append(item, factory(item));

        _empty.IsVisible = _controls.Count == 0;
        AttachEmptyIfNeeded();
    }

    private void Append(object item, Control row)
    {
        row.Tag = item;
        row.PointerPressed += OnRowPressed;
        row.PointerEntered += OnRowEntered;
        row.PointerExited += OnRowExited;

        _rows.Children.Add(row);
        _controls.Add(row);
        Paint(row);
    }

    private void AttachEmptyIfNeeded()
    {
        // 空列表时把提示放进滚动区，免得用户看到一个什么都没有的黑块。
        if (_rows.Children.Count == 0 && !_rows.Children.Contains(_empty))
        {
            _rows.Children.Add(_empty);
            _empty.IsVisible = true;
        }
        else if (_rows.Children.Count > 0 && _rows.Children.Contains(_empty))
        {
            _rows.Children.Remove(_empty);
        }
    }

    /// <summary>整表重建，用于条目数量变化。</summary>
    public void Rebuild<T>(IList<T> items, Func<T, Control> factory) where T : class
    {
        var keep = _selected;
        Bind(items, factory);
        if (keep is not null && _controls.Any(c => ReferenceEquals(c.Tag, keep)))
            Select(keep);
        else
            _selected = null;
    }

    public void Select(object? item)
    {
        if (ReferenceEquals(_selected, item))
        {
            PaintAll();
            return;
        }
        _selected = item;
        PaintAll();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PaintAll()
    {
        foreach (var row in _controls)
            Paint(row);
    }

    private void Paint(Control row)
    {
        if (row is not Border border)
            return;

        var isSelected = ReferenceEquals(row.Tag, _selected);
        var isHover = ReferenceEquals(row, _hovered);

        border.Background = isSelected
            ? SettingsUi.AccentSoftBrush
            : isHover ? SettingsUi.SurfaceHoverBrush : SettingsUi.SurfaceBrush;
        border.BorderBrush = isSelected ? SettingsUi.AccentBrush : SettingsUi.LineBrush;
        border.BorderThickness = new Thickness(isSelected ? 1 : 1);
        border.Opacity = ReferenceEquals(row, _pressed) && _dragging ? 0.72 : 1;
    }

    private Control? _hovered;

    private void OnRowEntered(object? sender, PointerEventArgs e)
    {
        _hovered = sender as Control;
        if (_hovered is not null)
            Paint(_hovered);
    }

    private void OnRowExited(object? sender, PointerEventArgs e)
    {
        var row = sender as Control;
        if (row is not null && ReferenceEquals(_hovered, row))
        {
            _hovered = null;
            Paint(row);
        }
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control row)
            return;
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            return;

        // 行里的输入控件要能正常点击，别把它们的按下当成拖拽起点。
        if (e.Source is TextBox or ComboBox or CheckBox or Button or Slider)
            return;

        _pressed = row;
        _pressY = e.GetPosition(_rows).Y;
        _dragIndex = _controls.IndexOf(row);
        _dragging = false;
        e.Pointer.Capture(this);

        if (row.Tag is { } item)
            Select(item);
        Paint(row);
        e.Handled = true;
    }

    private void OnListMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null)
            return;

        var y = e.GetPosition(_rows).Y;
        if (!_dragging)
        {
            if (Math.Abs(y - _pressY) < DragSlop)
                return;
            _dragging = true;
            Paint(_pressed);
        }

        var target = IndexAtY(y);
        if (target < 0 || target == _dragIndex || _dragIndex < 0)
            return;

        MoveRow(_dragIndex, target);
        _dragIndex = target;
        // 立刻重排一次布局，否则下一次 PointerMoved 还在用旧的行位置，
        // 会在相邻两行之间来回跳。
        _rows.UpdateLayout();
        e.Handled = true;
    }

    /// <summary>把行和模型同时挪一个位置，保证 Children 顺序与集合顺序始终一致。</summary>
    private void MoveRow(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _controls.Count || to >= _controls.Count)
            return;

        var row = _controls[from];
        _rows.Children.RemoveAt(from);
        _controls.RemoveAt(from);
        _rows.Children.Insert(to, row);
        _controls.Insert(to, row);

        if (_items is not null && from < _items.Count && to <= _items.Count)
        {
            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(Math.Min(to, _items.Count), item);
        }
    }

    private int IndexAtY(double y)
    {
        if (_controls.Count == 0)
            return -1;
        for (var i = 0; i < _controls.Count; i++)
        {
            var bounds = _controls[i].Bounds;
            if (y < bounds.Y + bounds.Height)
                return i;
        }
        return _controls.Count - 1;
    }

    private void OnListReleased(object? sender, PointerReleasedEventArgs e) => FinishDrag(e.Pointer);

    private void OnListCaptureLost(object? sender, PointerCaptureLostEventArgs e) => FinishDrag(null);

    private void FinishDrag(IPointer? pointer)
    {
        if (_pressed is null)
            return;

        var row = _pressed;
        var moved = _dragging;
        _pressed = null;
        _dragging = false;
        _dragIndex = -1;
        pointer?.Capture(null);
        Paint(row);

        if (moved)
        {
            if (Environment.GetEnvironmentVariable("ORBIT_TRACE") == "1")
                Log.Write($"[trace] 列表重排完成，共 {_controls.Count} 行");
            OrderChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

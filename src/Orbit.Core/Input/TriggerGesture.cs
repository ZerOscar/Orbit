// 手势的描述。刻意做得灵活：任意修饰键组合、任意鼠标键、可选额外按键、拖拽/长按/纯快捷键三种触发方式。
// 这一层不依赖任何操作系统 API，方便序列化到配置、也方便单测。

namespace Orbit.Core.Input;

[Flags]
public enum ModifierKeys
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    /// <summary>Windows 键 / macOS Command 键。</summary>
    Meta = 8,
}

public enum TriggerMouseButton
{
    None,
    Left,
    Right,
    Middle,
}

public enum TriggerKind
{
    /// <summary>按住修饰键并把鼠标拖过阈值。</summary>
    Drag,
    /// <summary>按住修饰键不动，停留指定毫秒。</summary>
    Hold,
    /// <summary>修饰键 + 某个键，不用鼠标。</summary>
    KeyChord,
}

/// <summary>打开环形菜单的手势。</summary>
public sealed class TriggerGesture
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public bool Enabled { get; set; } = true;
    public TriggerKind Kind { get; set; } = TriggerKind.Drag;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Ctrl;
    public TriggerMouseButton Button { get; set; } = TriggerMouseButton.Left;

    /// <summary>额外按键名（<see cref="VirtualKeys.NameOf"/> 的形式），KeyChord 必填。</summary>
    public string? Key { get; set; }

    /// <summary>拖拽触发所需的位移（像素），0 表示用系统默认。</summary>
    public int DragThreshold { get; set; }

    /// <summary>长按触发时长。</summary>
    public int HoldMilliseconds { get; set; } = 450;

    public TriggerGesture Clone() => (TriggerGesture)MemberwiseClone();

    /// <summary>
    /// 默认手势。快捷键 Ctrl+Alt+Space 用来"在光标处打开"；
    /// 拖拽用 Ctrl+Alt+左键——这个组合几乎没有系统语义，不会和各应用的拖放冲突。
    /// </summary>
    public static List<TriggerGesture> Defaults() =>
    [
        new()
        {
            Kind = TriggerKind.KeyChord,
            Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt,
            Key = "Space",
            Button = TriggerMouseButton.None,
        },
        new()
        {
            Kind = TriggerKind.Drag,
            Modifiers = ModifierKeys.Ctrl | ModifierKeys.Alt,
            Button = TriggerMouseButton.Left,
        },
    ];
}

// 一个动作 = 环上的一个切片。数据驱动，用户可以在配置文件里随便加减。

namespace Orbit.Core.Actions;

public enum ActionKind
{
    /// <summary>启动一个程序。</summary>
    Launch,
    /// <summary>在资源管理器里打开一个文件夹。</summary>
    OpenFolder,
    /// <summary>切换虚拟桌面（Target 为 next / prev）。</summary>
    SwitchDesktop,
    /// <summary>执行一条命令。</summary>
    RunCommand,
    /// <summary>锁定工作站。</summary>
    LockScreen,
}

public sealed class OrbitAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>环上显示的名字，尽量短（4 个字以内）。</summary>
    public string Label { get; set; } = "";

    /// <summary>标签上方的一个字符图标，留空则只显示文字。</summary>
    public string Glyph { get; set; } = "";

    public ActionKind Kind { get; set; } = ActionKind.Launch;

    /// <summary>程序路径、文件夹路径、命令或 next/prev。</summary>
    public string Target { get; set; } = "";

    public string? Arguments { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>鼠标悬停时中心显示的提示。</summary>
    public string? Hint { get; set; }

    public OrbitAction Clone() => (OrbitAction)MemberwiseClone();

    /// <summary>
    /// 开箱即用的一套动作。刻意只放系统自带、路径一定存在的目标——
    /// 装完就能按出东西来，比放一堆需要用户先配置的条目更有意义。
    /// </summary>
    public static List<OrbitAction> Defaults() =>
    [
        new() { Label = "文件", Glyph = "▤", Kind = ActionKind.Launch, Target = "explorer.exe", Hint = "打开资源管理器" },
        new() { Label = "终端", Glyph = "⌗", Kind = ActionKind.Launch, Target = "wt.exe", Arguments = null, Hint = "Windows Terminal（没装则回退 PowerShell）" },
        new() { Label = "计算器", Glyph = "▦", Kind = ActionKind.Launch, Target = "calc.exe", Hint = "计算器" },
        new() { Label = "设置", Glyph = "⚙", Kind = ActionKind.Launch, Target = "ms-settings:", Hint = "Windows 设置" },
        new() { Label = "下个桌面", Glyph = "→", Kind = ActionKind.SwitchDesktop, Target = "next", Hint = "Win+Ctrl+→" },
        new() { Label = "上个桌面", Glyph = "←", Kind = ActionKind.SwitchDesktop, Target = "prev", Hint = "Win+Ctrl+←" },
        new() { Label = "锁屏", Glyph = "⊙", Kind = ActionKind.LockScreen, Target = "", Hint = "锁定工作站" },
        new() { Label = "下载", Glyph = "↓", Kind = ActionKind.OpenFolder, Target = "%USERPROFILE%\\Downloads", Hint = "打开下载文件夹" },
    ];
}

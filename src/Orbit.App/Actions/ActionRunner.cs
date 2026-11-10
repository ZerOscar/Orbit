// 动作执行。每个分支都必须能失败而不崩——目标路径不存在是最常见的情况。

using System.Diagnostics;
using System.Runtime.InteropServices;
using Orbit.Core.Actions;

namespace Orbit.App.Actions;

public sealed class ActionRunner
{
    /// <summary>最近一次失败的原因，供界面提示。</summary>
    public string? LastError { get; private set; }

    public bool Run(OrbitAction action)
    {
        LastError = null;
        try
        {
            return action.Kind switch
            {
                ActionKind.Launch => Launch(action),
                ActionKind.OpenFolder => OpenFolder(action),
                ActionKind.SwitchDesktop => SwitchDesktop(action.Target),
                ActionKind.RunCommand => RunCommand(action),
                ActionKind.LockScreen => Lock(),
                _ => Fail($"未知的动作类型：{action.Kind}"),
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Fail(ex.Message);
        }
    }

    private bool Launch(OrbitAction action)
    {
        var target = Expand(action.Target);
        if (string.IsNullOrWhiteSpace(target))
            return Fail("目标为空");

        // 带协议的目标（ms-settings: 之类）交给 shell；普通程序用 UseShellExecute 才能继承环境与工作目录。
        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
        };
        if (!string.IsNullOrWhiteSpace(action.Arguments))
            startInfo.Arguments = action.Arguments;

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 常见情况：目标程序没装（wt.exe 就是典型）。回退到 PowerShell，别让按钮点下去毫无反应。
            if (Path.GetFileName(target).Equals("wt.exe", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = true });
                return true;
            }
            return Fail($"启动失败：{target}");
        }
    }

    private bool OpenFolder(OrbitAction action)
    {
        var path = Expand(action.Target);
        if (!Directory.Exists(path))
            return Fail($"文件夹不存在：{path}");
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        return true;
    }

    private bool RunCommand(OrbitAction action)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {Expand(action.Target)}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(startInfo);
        return true;
    }

    private bool Lock()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = "user32.dll,LockWorkStation",
            UseShellExecute = true,
        });
        return true;
    }

    /// <summary>
    /// 切换虚拟桌面。这里刻意模拟 Win+Ctrl+←/→ 而不是调用私有的
    /// IVirtualDesktopManagerInternal——那个接口每个 Windows 大版本都会换虚表，
    /// 用它换来的「直接跳到第 N 个桌面」不值得承担随时失效的风险。
    /// </summary>
    private bool SwitchDesktop(string direction)
    {
        var forward = !string.Equals(direction, "prev", StringComparison.OrdinalIgnoreCase);
        var arrow = forward ? VkRight : VkLeft;

        SendKey(VkLWin, down: true);
        SendKey(VkControl, down: true);
        SendKey(arrow, down: true);
        SendKey(arrow, down: false);
        SendKey(VkControl, down: false);
        SendKey(VkLWin, down: false);
        return true;
    }

    private static string Expand(string value) =>
        Environment.ExpandEnvironmentVariables(value ?? string.Empty).Trim();

    private bool Fail(string message)
    {
        LastError = message;
        return false;
    }

    // ───────────── SendInput ─────────────

    private const ushort VkControl = 0x11;
    private const ushort VkLWin = 0x5B;
    private const ushort VkLeft = 0x25;
    private const ushort VkRight = 0x27;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    private static void SendKey(ushort virtualKey, bool down)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = down ? 0 : KeyEventKeyUp,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero,
                },
            },
        };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}

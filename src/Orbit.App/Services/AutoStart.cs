// 开机自启：往 HKCU 的 Run 项写一条命令行，不需要管理员权限。
//
// 配置里的 StartWithWindows 是「用户意图」，注册表是「当前实际状态」。
// 两者在启动时和保存设置时对齐一次，这样做的好处是：注册表被清理工具删掉了，
// 下次启动会自己补回来；用户想关掉，也不会因为残留项又启动。

using Microsoft.Win32;

namespace Orbit.App.Services;

internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Orbit";

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// 单文件发布下 Assembly.Location 是空串，只能靠 ProcessPath 拿到真实 exe 路径。
    /// </summary>
    private static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>注册表里登记的命令行，没有登记返回 null。</summary>
    public static string? RegisteredCommand()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(ex, "读取自启项失败");
            return null;
        }
    }

    /// <summary>
    /// 已登记、且指向当前这个 exe，才算真的会开机启动。
    /// 只看值存不存在是不够的——用户把 exe 挪了位置，旧路径会指向一个不存在的文件。
    /// </summary>
    public static bool IsEnabled()
    {
        var command = RegisteredCommand();
        var exe = ExecutablePath;
        if (command is null || exe is null)
            return false;
        return Normalize(command).Equals(exe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把注册表对齐到期望状态。返回是否成功，失败时 error 给出原因。
    /// 状态已经一致就直接返回，不重复写注册表。
    /// </summary>
    public static bool Sync(bool enabled, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "仅支持 Windows";
            return false;
        }

        var exe = ExecutablePath;
        if (string.IsNullOrEmpty(exe))
        {
            error = "取不到当前程序路径";
            return false;
        }

        try
        {
            if (enabled)
            {
                if (IsEnabled())
                    return true;

                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key is null)
                {
                    error = "无法打开注册表 Run 项";
                    return false;
                }

                // 路径一定要带引号：Program Files 里有空格，不加引号会被截断成第一个单词。
                key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);

                if (IsEnabled())
                    return true;

                error = "写入后校验未通过";
                return false;
            }

            if (RegisteredCommand() is null)
                return true;

            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                key?.DeleteValue(ValueName, throwOnMissingValue: false);

            if (RegisteredCommand() is null)
                return true;

            error = "删除后仍有残留";
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            // 安全软件拦截写入是很常见的情况，原样把原因带给调用方。
            error = ex.Message;
            return false;
        }
    }

    /// <summary>把带引号、可能带参数的命令行还原成纯 exe 路径，用于比较。</summary>
    internal static string Normalize(string command)
    {
        var trimmed = command.Trim();

        // 先按 .exe 截断：它可能被引号包着，后面也可能跟着启动参数。
        // 顺序很重要——先去引号的话，带参数的命令会留下一个孤立的前引号。
        var index = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (index > 0)
            trimmed = trimmed[..(index + 4)];

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return trimmed[1..^1];
        return trimmed.Length >= 1 && trimmed[0] == '"' ? trimmed[1..] : trimmed;
    }
}

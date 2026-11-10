// 极简日志：只写文件，不加依赖。出问题的时候这是唯一能看到的东西。

using System.Text;
using Orbit.Core.Config;

namespace Orbit.App.Services;

public static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(ConfigStore.DefaultPath)!, "orbit.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = System.IO.Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // 日志本身失败不能影响主流程。
        }
    }

    public static void Write(Exception exception, string context)
    {
        Write($"{context}: {exception.GetType().Name}: {exception.Message}");
        Write(exception.StackTrace ?? "(no stack)");
    }
}

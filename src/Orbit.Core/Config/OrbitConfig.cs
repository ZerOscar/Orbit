// 配置：手势 + 动作 + 外观。存 JSON，带 schema 版本号，将来改结构好迁移。

using System.Text.Json;
using System.Text.Json.Serialization;
using Orbit.Core.Actions;
using Orbit.Core.Input;

namespace Orbit.Core.Config;

public sealed class OrbitConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<TriggerGesture> Triggers { get; set; } = TriggerGesture.Defaults();

    public List<OrbitAction> Actions { get; set; } = OrbitAction.Defaults();

    /// <summary>环的外半径（像素）。</summary>
    public double OuterRadius { get; set; } = 132;

    /// <summary>中心孔洞占比。</summary>
    public double HoleRatio { get; set; } = 0.42;

    public double SliceOpacity { get; set; } = 0.92;

    public bool Paused { get; set; }

    /// <summary>开机自启。真正生效的是注册表 Run 项，这里只记录用户的意图。</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>启动时静默检查新版本。默认开，检查失败不会打扰用户。</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>把明显无效的配置修正回可用状态，不抛异常。</summary>
    public void EnsureConsistency()
    {
        if (Triggers.Count == 0)
            Triggers = TriggerGesture.Defaults();
        if (Actions.Count == 0)
            Actions = OrbitAction.Defaults();
        Actions.RemoveAll(a => string.IsNullOrWhiteSpace(a.Label));
        OuterRadius = Math.Clamp(OuterRadius, 80, 220);
        HoleRatio = Math.Clamp(HoleRatio, 0.2, 0.7);
        SliceOpacity = Math.Clamp(SliceOpacity, 0.3, 1);
        SchemaVersion = CurrentSchemaVersion;
    }

    public OrbitConfig Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Triggers = Triggers.Select(t => t.Clone()).ToList(),
        Actions = Actions.Select(a => a.Clone()).ToList(),
        OuterRadius = OuterRadius,
        HoleRatio = HoleRatio,
        SliceOpacity = SliceOpacity,
        Paused = Paused,
        StartWithWindows = StartWithWindows,
        CheckForUpdates = CheckForUpdates,
    };
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orbit", "config.json");

    /// <summary>读取配置；文件缺失或损坏时返回默认配置，并把坏文件改名保留。</summary>
    public static OrbitConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return new OrbitConfig();

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<OrbitConfig>(json, Options) ?? new OrbitConfig();
            config.EnsureConsistency();
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            TryKeepBroken(path);
            return new OrbitConfig();
        }
    }

    public static void Save(OrbitConfig config, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // 先写临时文件再替换，避免写到一半断电留下半个 JSON。
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryKeepBroken(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, path + ".broken", overwrite: true);
        }
        catch (IOException)
        {
            // 保留失败就算了，不影响启动。
        }
    }
}

// 更新检查：问一次 GitHub Releases 的 latest，比版本号，不下载任何东西。
//
// 三条纪律：
// 1. 失败一律静默。这是个托盘常驻工具，网络不通不该弹任何东西。
// 2. 24 小时内只问一次。GitHub 匿名接口每小时 60 次，不能每个进程都去问。
// 3. 同一个版本只提醒一次。否则每次开机都弹，用户会直接关掉这个功能。

using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Orbit.App.Services;

internal enum UpdateStatus
{
    /// <summary>已是最新。</summary>
    UpToDate,

    /// <summary>发现新版本。</summary>
    Available,

    /// <summary>网络或解析失败，已静默。</summary>
    Failed,

    /// <summary>在节流窗口内，本次没有真的去问。</summary>
    Skipped,

    /// <summary>用户关掉了自动检查。</summary>
    Disabled,
}

internal sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Url,
    DateTimeOffset? Published,
    string? Notes)
{
    public string VersionText => UpdateChecker.Format(Version);
}

internal sealed record UpdateResult(UpdateStatus Status, UpdateInfo? Update, string Message);

/// <summary>
/// 检查 GitHub 上的最新 Release。
/// </summary>
internal sealed class UpdateChecker
{
    public const string Repository = "ZerOscar/Orbit";

    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Repository}/releases/latest";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private static readonly HttpClient Http = CreateClient();

    private readonly string _statePath;
    private bool _stateLoaded;
    private DateTimeOffset _lastChecked;
    private string? _lastNotifiedTag;

    public UpdateChecker(string? statePath = null)
    {
        _statePath = statePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orbit", "update.json");
    }

    /// <summary>当前这个进程的版本，来自 csproj 的 Version 属性。</summary>
    public static Version CurrentVersion { get; } = ReadCurrentVersion();

    public static string CurrentVersionText => Format(CurrentVersion);

    /// <summary>
    /// 去问一次最新版本。force 为 true 时忽略 24 小时节流（设置页的「立即检查」用它）。
    /// 任何异常都被吃掉，转成 Failed。
    /// </summary>
    public async Task<UpdateResult> CheckAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        LoadState();

        if (!force)
        {
            var elapsed = DateTimeOffset.UtcNow - _lastChecked;
            if (elapsed < Interval)
                return new UpdateResult(UpdateStatus.Skipped, null, $"最近已经检查过，{Remaining(elapsed)}后再试");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            // GitHub 会对没有 User-Agent 的请求直接 403，这一行不能省。
            request.Headers.UserAgent.ParseAdd($"Orbit/{CurrentVersionText}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            _lastChecked = DateTimeOffset.UtcNow;

            if (!response.IsSuccessStatusCode)
            {
                SaveState();
                Log.Write($"更新检查失败：HTTP {(int)response.StatusCode}");
                return new UpdateResult(UpdateStatus.Failed, null, $"服务器返回 {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var info = Parse(json);
            SaveState();

            if (info is null)
                return new UpdateResult(UpdateStatus.Failed, null, "返回内容看不懂");

            if (info.Version > CurrentVersion)
            {
                Log.Write($"发现新版本 {info.Tag}（当前 {CurrentVersionText}）");
                return new UpdateResult(UpdateStatus.Available, info, $"发现新版本 {info.Tag}");
            }

            return new UpdateResult(UpdateStatus.UpToDate, null, $"已是最新版本 {CurrentVersionText}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // 超时会被包装成 TaskCanceledException，和用户取消要区分开，所以上面先 rethrow。
            _lastChecked = DateTimeOffset.UtcNow;
            SaveState();
            Log.Write(ex, "更新检查失败");
            return new UpdateResult(UpdateStatus.Failed, null, "连不上更新服务器");
        }
    }

    /// <summary>
    /// 这个版本还没提醒过。后台检查拿到结果后用它决定要不要弹提示，
    /// 保证同一个版本只打扰一次。
    /// </summary>
    public bool ShouldNotify(UpdateInfo info)
    {
        LoadState();
        return !string.Equals(_lastNotifiedTag, info.Tag, StringComparison.OrdinalIgnoreCase);
    }

    public void MarkNotified(UpdateInfo info)
    {
        LoadState();
        _lastNotifiedTag = info.Tag;
        SaveState();
    }

    // ───────────── 解析 ─────────────

    /// <summary>解析 GitHub 的 release JSON。internal 是为了让单测能直接喂样本。</summary>
    internal static UpdateInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement) ||
            tagElement.GetString() is not { Length: > 0 } tag)
        {
            return null;
        }

        var version = System.Version.TryParse(tag.TrimStart('v', 'V'), out var parsed) ? parsed : null;
        if (version is null)
            return null;

        var url = root.TryGetProperty("html_url", out var urlElement)
            ? urlElement.GetString() ?? ""
            : $"https://github.com/{Repository}/releases";

        DateTimeOffset? published = null;
        if (root.TryGetProperty("published_at", out var publishedElement) &&
            publishedElement.ValueKind == JsonValueKind.String &&
            publishedElement.TryGetDateTimeOffset(out var moment))
        {
            published = moment;
        }

        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;

        // 只比前三段，第四段是程序集版本里的 revision，Release 不会带。
        return new UpdateInfo(Normalize(version), tag, url, published, notes);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    /// <summary>统一显示成三段（1.0.0 / 1.1.0），和 Release 的 tag 对得上。</summary>
    public static string Format(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private static Version ReadCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly;
        // InformationalVersion 可能被 CI 追加 "+abc1234"，Version 属性是干净的。
        return Normalize(assembly.GetName().Version ?? new Version(1, 0, 0));
    }

    private static string Remaining(TimeSpan elapsed)
    {
        var left = Interval - elapsed;
        return left.TotalHours >= 1 ? $"{left.TotalHours:0} 小时" : $"{Math.Max(1, left.TotalMinutes):0} 分钟";
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            // 检查更新不该卡住任何东西，8 秒还没回就当失败。
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        return client;
    }

    // ───────────── 状态文件 ─────────────

    private void LoadState()
    {
        if (_stateLoaded)
            return;
        _stateLoaded = true;

        try
        {
            if (!File.Exists(_statePath))
                return;

            using var document = JsonDocument.Parse(File.ReadAllText(_statePath));
            var root = document.RootElement;
            if (root.TryGetProperty("lastCheckedUtc", out var checkedElement) &&
                checkedElement.TryGetDateTimeOffset(out var checkedAt))
            {
                _lastChecked = checkedAt;
            }

            if (root.TryGetProperty("lastNotifiedTag", out var tagElement))
                _lastNotifiedTag = tagElement.GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 状态文件坏了不是什么大事，当作没检查过。
            Log.Write(ex, "读取更新状态失败");
        }
    }

    private void SaveState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var json = JsonSerializer.Serialize(new
            {
                lastCheckedUtc = _lastChecked,
                lastNotifiedTag = _lastNotifiedTag,
            });
            File.WriteAllText(_statePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(ex, "保存更新状态失败");
        }
    }

    /// <summary>给日志用的耗时描述，调试时能看出检查卡在哪。</summary>
    public static string Describe(UpdateResult result) => $"{result.Status} · {result.Message}";
}

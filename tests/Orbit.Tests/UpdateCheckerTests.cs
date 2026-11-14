// 更新检查与自启的纯逻辑测试：解析、版本比较、路径归一化。
// 真正发网络请求和写注册表的部分不在这里测——那属于集成验证。

using Orbit.App.Services;

namespace Orbit.Tests;

public class UpdateCheckerTests
{
    private const string SampleRelease = """
        {
          "tag_name": "v1.1.0",
          "html_url": "https://github.com/ZerOscar/Orbit/releases/tag/v1.1.0",
          "published_at": "2026-11-14T01:30:00Z",
          "body": "- 开机自启\n- 更新提醒"
        }
        """;

    [Fact]
    public void Parse_ReadsTagUrlAndDate()
    {
        var info = UpdateChecker.Parse(SampleRelease);

        Assert.NotNull(info);
        Assert.Equal("v1.1.0", info!.Tag);
        Assert.Equal(new Version(1, 1, 0), info.Version);
        Assert.Equal("https://github.com/ZerOscar/Orbit/releases/tag/v1.1.0", info.Url);
        Assert.NotNull(info.Published);
        Assert.Equal(2026, info.Published!.Value.Year);
        Assert.Contains("开机自启", info.Notes);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("v1.1.0")]
    [InlineData("V1.1.0")]
    public void Parse_AcceptsTagWithOrWithoutPrefix(string tag)
    {
        var json = $$"""{ "tag_name": "{{tag}}", "html_url": "https://github.com/x/y" }""";

        var info = UpdateChecker.Parse(json);

        Assert.NotNull(info);
        Assert.Equal(new Version(1, 1, 0), info!.Version);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenTagMissing()
    {
        var info = UpdateChecker.Parse("""{ "html_url": "https://github.com/x/y" }""");
        Assert.Null(info);
    }

    [Fact]
    public void Parse_ReturnsNull_ForPrereleaseTag()
    {
        // 预发布版本号解析不出来，正好当作「不提醒」，避免把 beta 推给普通用户。
        var info = UpdateChecker.Parse("""{ "tag_name": "v1.2.0-beta.1" }""");
        Assert.Null(info);
    }

    [Fact]
    public void Parse_FallsBackToReleasesPage_WhenUrlMissing()
    {
        var info = UpdateChecker.Parse("""{ "tag_name": "v1.1.0" }""");

        Assert.NotNull(info);
        Assert.Equal($"https://github.com/{UpdateChecker.Repository}/releases", info!.Url);
    }

    [Fact]
    public void Version_Comparison_IsNumericNotLexicographic()
    {
        // "1.10.0" 按字符串比会小于 "1.9.0"，版本号必须按数字比。
        Assert.True(new Version(1, 10, 0) > new Version(1, 9, 0));
        Assert.True(new Version(1, 1, 0) > new Version(1, 0, 0));
        Assert.False(new Version(1, 0, 0) > new Version(1, 0, 0));
    }

    [Theory]
    [InlineData(1, 1, 0, "1.1.0")]
    [InlineData(1, 0, 0, "1.0.0")]
    [InlineData(2, 3, 4, "2.3.4")]
    public void Format_AlwaysShowsThreeParts(int major, int minor, int build, string expected)
    {
        Assert.Equal(expected, UpdateChecker.Format(new Version(major, minor, build)));
    }

    [Fact]
    public void CurrentVersion_IsReadFromAssembly()
    {
        Assert.True(UpdateChecker.CurrentVersion.Major >= 1);
        Assert.Matches(@"^\d+\.\d+\.\d+$", UpdateChecker.CurrentVersionText);
    }
}

public class AutoStartTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Orbit\\Orbit.exe\"", "C:\\Program Files\\Orbit\\Orbit.exe")]
    [InlineData("C:\\Orbit\\Orbit.exe", "C:\\Orbit\\Orbit.exe")]
    [InlineData("\"C:\\Orbit\\Orbit.exe\" --minimized", "C:\\Orbit\\Orbit.exe")]
    [InlineData("  \"C:\\Orbit\\Orbit.exe\"  ", "C:\\Orbit\\Orbit.exe")]
    public void Normalize_StripsQuotesAndArguments(string command, string expected)
    {
        Assert.Equal(expected, AutoStart.Normalize(command));
    }

    [Fact]
    public void Normalize_PreservesCase_SoCallersMustCompareIgnoringIt()
    {
        // 注册表里可能写成 .EXE。Normalize 只负责去引号和参数，
        // 大小写交给 IsEnabled 用 OrdinalIgnoreCase 处理。
        var normalized = AutoStart.Normalize("\"C:\\Orbit\\Orbit.EXE\"");

        Assert.Equal("C:\\Orbit\\Orbit.exe", normalized, ignoreCase: true);
        Assert.NotEqual("C:\\Orbit\\Orbit.exe", normalized);
    }
}

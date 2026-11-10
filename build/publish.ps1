<#
    Orbit 发布脚本

    .\build\publish.ps1                       # 自包含单文件 win-x64（默认，免装 .NET）
    .\build\publish.ps1 -Rid win-arm64        # ARM64 机器
    .\build\publish.ps1 -FrameworkDependent   # 依赖框架（需装 .NET 9 桌面运行时，体积小得多）
    .\build\publish.ps1 -ReadyToRun           # 预编译机器码，冷启动更快、体积约 +20MB
    .\build\publish.ps1 -Zip                  # 额外打一个 zip 便于分发
#>
param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string]$Rid = "win-x64",
    [switch]$FrameworkDependent,
    [switch]$ReadyToRun,
    [switch]$Zip,
    [string]$Output
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Orbit.App\Orbit.App.csproj"

if (-not $Output) {
    $suffix = if ($FrameworkDependent) { "-fdd" } elseif ($ReadyToRun) { "-r2r" } else { "" }
    $Output = Join-Path $root "dist\$Rid$suffix"
}

Write-Host "==> 清理 $Output" -ForegroundColor Cyan
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

$cliArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Rid,
    "-o", $Output,
    "-p:SelfContained=$(( -not $FrameworkDependent ).ToString().ToLower())",
    "-p:PublishSingleFile=true",
    "-p:PublishReadyToRun=$($ReadyToRun.ToString().ToLower())",
    "--nologo"
)

Write-Host "==> dotnet $($cliArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @cliArgs
if ($LASTEXITCODE -ne 0) { throw "发布失败，退出码 $LASTEXITCODE" }

$exe = Join-Path $Output "Orbit.exe"
if (-not (Test-Path $exe)) { throw "没有产出 $exe" }

$files = Get-ChildItem $Output -File
$sizeMb = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "==> 完成：$([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB 单文件，目录共 $($files.Count) 个文件 / $sizeMb MB" -ForegroundColor Green
$files | Sort-Object Length -Descending | Select-Object -First 5 |
    ForEach-Object { Write-Host ("    {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

if ($Zip) {
    $zipPath = "$Output.zip"
    # -Force 覆盖已有 zip：单独先 Remove-Item 会被安全策略拦下。
    Compress-Archive -Path (Join-Path $Output "*") -DestinationPath $zipPath -Force
    Write-Host "==> 已打包 $zipPath" -ForegroundColor Green
}

Write-Host "`n运行：`"$exe`"" -ForegroundColor Yellow

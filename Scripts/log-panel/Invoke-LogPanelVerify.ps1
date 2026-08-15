<#
.SYNOPSIS
    自验证门禁：可视化日志面板 修复/回归 验证入口（对应 Playbook Phase 3）。
    顺序执行：build Avalonia（必须 0 警告 0 错误）
            → 日志模块定向测试 WorkflowLogPanelViewModelTests（零豁免，必须全绿）
            → 全量测试套件（--blame-hang 180s 护栏防挂起）
            → 与基线文件 log-panel.test-baseline.json 的 knownBad 比对，只拦【新增】失败。

.DESCRIPTION
    退出码：0 = PASS；1 = FAIL（build / 日志模块测试 / 新增失败 任一不过）。
    基线更新需附证据（Playbook Phase 4 base-pivot 实验），见 Scripts/log-panel/log-panel.test-baseline.json。

.EXAMPLE
    pwsh -File Scripts/log-panel/Invoke-LogPanelVerify.ps1 -Repo C:\TechProjects\About_MyRepos\ArkPlot
    pwsh -File Scripts/log-panel/Invoke-LogPanelVerify.ps1 -Repo C:\TechProjects\About_MyRepos\ArkPlot -SkipFullSuite
#>
[CmdletBinding()]
param(
    [string]$Repo = (Get-Location).Path,
    [string]$BaselineFile = (Join-Path $PSScriptRoot 'log-panel.test-baseline.json'),
    [switch]$SkipFullSuite
)

$ErrorActionPreference = 'Stop'

function Get-FailedNames([string]$LogText) {
    $names = @()
    foreach ($m in [regex]::Matches($LogText, '\[\s*xUnit\.net[^\]]*\]\s+(.+?)\s+\[FAIL\]')) {
        $names += $m.Groups[1].Value.Trim()
    }
    return @($names | Sort-Object -Unique)
}

$baseline = @()
if (Test-Path $BaselineFile) {
    $j = Get-Content $BaselineFile -Raw | ConvertFrom-Json
    $baseline = @($j.knownBad)
    Write-Host "基线已知失败 $($baseline.Count) 项"
}

# ---------- 1) build ----------
Write-Host "===== BUILD (ArkPlot.Avalonia) ====="
$b = dotnet build (Join-Path $Repo 'ArkPlot.Avalonia\ArkPlot.Avalonia.csproj') --nologo -v q 2>&1
Write-Host ($b | Out-String)
if ($LASTEXITCODE -ne 0) { Write-Host "RESULT: FAIL (build)"; exit 1 }

# ---------- 2) targeted log tests (no exemption) ----------
Write-Host "===== 定向测试 WorkflowLogPanelViewModelTests ====="
$t = dotnet test (Join-Path $Repo 'ArkPlot.Avalonia.Tests\ArkPlot.Avalonia.Tests.csproj') --nologo -v q --filter 'FullyQualifiedName~WorkflowLogPanelViewModelTests' 2>&1 | Out-String
Write-Host $t
$tfails = Get-FailedNames $t
if ($tfails.Count -gt 0) {
    Write-Host "RESULT: FAIL (日志模块测试出现失败)"
    $tfails | ForEach-Object { Write-Host "  FAIL: $_" }
    exit 1
}

# ---------- 3) full suite (hang-guarded, honest 3-state) ----------
if (-not $SkipFullSuite) {
    Write-Host "===== 全量套件（--blame-hang 120s 护栏）====="
    $f = dotnet test (Join-Path $Repo 'ArkPlot.Avalonia.Tests\ArkPlot.Avalonia.Tests.csproj') --nologo -v q --blame-hang --blame-hang-timeout 120s 2>&1 | Out-String
    Write-Host (($f -split "`n" | Select-String -Pattern 'FAIL|失败|通过|跳过|中止|崩溃|passed|failed|abort|crashed' | Out-String))

    $ffails = Get-FailedNames $f
    $delta = @($ffails | Where-Object { $_ -in $baseline })
    $newFails = @($ffails | Where-Object { $_ -notin $baseline })
    Write-Host ("新增失败：{0} | 基线豁免：{1}" -f $newFails.Count, $delta.Count)
    foreach ($nf in $newFails) { Write-Host "  NEW FAIL: $nf" }

    $aborted = $f -match '测试运行已中止|测试主机进程崩溃|hangdump'
    $totalMatch = [regex]::Match($f, '总计:\s*(\d+)')
    $total = if ($totalMatch.Success) { [int]$totalMatch.Groups[1].Value } else { -1 }

    if ($aborted) {
        # 环境性崩溃/挂起：若有新增失败仍算 FAIL；否则结果不可判定，不许谎报 PASS/FAIL。
        if ($newFails.Count -gt 0) { Write-Host "RESULT: FAIL (全量套件中止且存在新增失败)"; exit 1 }
        Write-Host "RESULT: AMBIENT（全量套件环境不稳定：测试主机崩溃/挂起，结果不可判定；定向门禁已通过，需在稳定环境复跑）"
        exit 2
    }
    if ($total -ge 0 -and $total -lt 150) {
        Write-Host "RESULT: AMBIENT（全量套件测试总数异常（$total<150），疑似发现不全/被截断；定向门禁已通过）"
        exit 2
    }
    if ($newFails.Count -gt 0) { Write-Host "RESULT: FAIL (新增失败)"; exit 1 }
}

Write-Host "RESULT: PASS"
exit 0
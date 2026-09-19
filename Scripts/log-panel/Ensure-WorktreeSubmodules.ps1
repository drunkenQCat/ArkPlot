<#
.SYNOPSIS
    自修复子模块初始化（worktree 版）。
    标准 `git submodule update` 在子模块远程提交被上游重写时会失败
    （fatal: upload-pack: not our ref <sha>），本脚本自动降级为：
    从本地主仓库的现有子模块 clone 到目标 worktree，并检出到 gitlink 记录提交。
    附带本地 clone 所需的环境开关（-c protocol.file.allow=always，仅本次调用生效）。
    可选 -VerifyOnly：只巡检现状（是否检出 / HEAD 是否等于 gitlink），不写盘。

.DESCRIPTION
    对应 Playbook docs/log-panel-fix-playbook.md 自修复手册 #1/#2/#3。
    用法示例：
      # 修复（含克隆）：针对多个 worktree
      pwsh -File Scripts/log-panel/Ensure-WorktreeSubmodules.ps1 `
           -WorktreeRoots C:\wt\log-core,C:\wt\log-view `
           -SourceRepo C:\TechProjects\About_MyRepos\ArkPlot
      # 只巡检
      pwsh -File Scripts/log-panel/Ensure-WorktreeSubmodules.ps1 `
           -WorktreeRoots C:\wt\log-core -VerifyOnly

.EXITCODE
    0 = 全部就绪（或巡检全部 OK）；1 = 存在失败/未检出。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$WorktreeRoots,

    [string]$SourceRepo = (Get-Location).Path,

    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'

function Get-SubmodulePaths([string]$Repo) {
    $gitmodules = Join-Path $Repo '.gitmodules'
    if (-not (Test-Path $gitmodules)) { return @() }
    $lines = git -C $Repo config --file $gitmodules --get-regexp '^submodule\..*\.path$' 2>$null
    $paths = @()
    foreach ($l in $lines) {
        $parts = $l -split '\s+', 2
        if ($parts.Count -eq 2) { $paths += $parts[1].Trim() }
    }
    return $paths
}

function Get-Gitlink([string]$Repo, [string]$Path) {
    # 注意：单行输出时 $out 是 string 而非数组，$out[0] 会取到首个字符——用正则解析整行。
    $out = git -C $Repo ls-tree HEAD -- $Path 2>$null
    if (-not $out) { return $null }
    $line = [string]$out
    if ($line -match 'commit\s+([0-9a-f]{40})') { return $Matches[1] }
    return $null
}

$sep = [IO.Path]::DirectorySeparatorChar
$allOk = $true

foreach ($wt in $WorktreeRoots) {
    Write-Host "===== $wt ====="
    if (-not (Test-Path (Join-Path $wt '.git'))) { Write-Host "  不是 git 工作树，跳过"; continue }

    $paths = Get-SubmodulePaths $wt
    if ($paths.Count -eq 0) { Write-Host "  无子模块"; continue }

    foreach ($path in $paths) {
        $dst = Join-Path $wt ($path -replace '/', $sep)
        $gitlink = Get-Gitlink $wt $path
        $head = $null
        if (Test-Path (Join-Path $dst '.git')) { $head = git -C $dst rev-parse HEAD 2>$null }

        Write-Host "  [$path] gitlink=$gitlink"

        if ($VerifyOnly) {
            if (-not $head) { Write-Host "    MISSING（未检出）"; $allOk = $false }
            elseif ($head -ne $gitlink) { Write-Host "    MISMATCH head=$head"; $allOk = $false }
            else { Write-Host "    OK" }
            continue
        }

        if ($head -and $head -eq $gitlink) { Write-Host "    OK（已就绪）"; continue }

        # 清理残留（含被占用目录的重试）
        for ($i = 0; $i -lt 5 -and (Test-Path $dst); $i++) {
            Remove-Item -Recurse -Force $dst -ErrorAction SilentlyContinue
            if (Test-Path $dst) { Start-Sleep -Seconds 2 }
        }
        if (Test-Path $dst) { Write-Host "    FAIL：目录被占用，无法清理"; $allOk = $false; continue }

        $src = Join-Path $SourceRepo ($path -replace '/', $sep)
        if (-not (Test-Path (Join-Path $src '.git'))) {
            Write-Host "    FAIL：本地源不存在 $src"; $allOk = $false; continue
        }

        Write-Host "    从本地 clone：$src"
        git -c protocol.file.allow=always clone --quiet $src $dst
        if ($LASTEXITCODE -ne 0) { Write-Host "    FAIL：clone 失败"; $allOk = $false; continue }

        $head = git -C $dst rev-parse HEAD
        if ($head -ne $gitlink) {
            Write-Host "    检出 $gitlink"
            git -C $dst checkout --quiet $gitlink
            if ($LASTEXITCODE -ne 0) { Write-Host "    FAIL：checkout 失败"; $allOk = $false; continue }
            $head = git -C $dst rev-parse HEAD
        }
        if ($head -eq $gitlink) { Write-Host "    FIXED OK" }
        else { Write-Host "    FAIL：head=$head"; $allOk = $false }
    }
}

if ($allOk) { Write-Host "===== 全部就绪 ====="; exit 0 }
else { Write-Host "===== 存在失败 ====="; exit 1 }
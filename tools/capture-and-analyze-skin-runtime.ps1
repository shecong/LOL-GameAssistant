#Requires -Version 5.1
<#
Elevated entry point: capture the running match, then hand the snapshot to the
report and to the compiled resolver.

The game process runs at High integrity, so the read-only message probe must be
started from an elevated process as well (UIPI rejects PostMessage from Medium).
Run this from an administrator PowerShell -- tools\capture-skin-runtime-elevated.cmd
does that with one right-click.
#>
param(
    [int]$WaitMinutes = 20,
    [int]$IntervalSeconds = 5,
    [switch]$Pause
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$log = Join-Path $root 'tmp\skin-core-capture.log'
New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null

function Write-Step([string]$text) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $text
    Write-Host $line
    Add-Content -LiteralPath $log -Value $line -Encoding utf8
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Step "启动：管理员=$elevated（游戏进程为 High 完整性，必须管理员）"
if (-not $elevated) {
    Write-Step '未提权，探针会被 UIPI 拒绝。请以管理员身份重新运行。'
    if ($Pause) { Read-Host '按回车关闭' }
    exit 1
}

try {
    $manifestPath = & (Join-Path $root 'tools\capture-skin-runtime.ps1') -WaitMinutes $WaitMinutes -IntervalSeconds $IntervalSeconds
    $captureDir = Split-Path -Parent $manifestPath
    Write-Step "快照目录：$captureDir"

    $python = 'C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
    if (-not (Test-Path -LiteralPath $python)) { $python = 'python' }
    $capstone = Join-Path $root '.artifacts\rank-border-20261026\python-deps'
    if (Test-Path -LiteralPath (Join-Path $capstone 'capstone')) {
        $env:PYTHONPATH = if ($env:PYTHONPATH) { "$capstone;$env:PYTHONPATH" } else { $capstone }
    }
    $env:PYTHONIOENCODING = 'utf-8'
    Write-Step '生成规则对照报告…'
    $report = Join-Path $captureDir 'report.json'
    & $python (Join-Path $root 'tools\report-skin-runtime-capture.py') --capture $captureDir --json $report 2>&1 |
        ForEach-Object { Write-Host $_; Add-Content -LiteralPath $log -Value $_ -Encoding utf8 }

    Write-Step '用编译后的核心解析该快照…'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\validate-skin-runtime-capture.ps1') -Capture $captureDir 2>&1 |
        ForEach-Object { Write-Host $_; Add-Content -LiteralPath $log -Value $_ -Encoding utf8 }
    Write-Step '完成。'
} catch {
    Write-Step "失败：$($_.Exception.Message)"
} finally {
    if ($Pause) { Read-Host '按回车关闭' }
}

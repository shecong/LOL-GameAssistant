#Requires -Version 5.1
<#
Read-only capture of the running game's executable section for skin-core adaptation.

The game protects its on-disk image (ACE renames every section to `.ace` and stores
the executable section compressed/encrypted), so locator rules can only be updated
from the *loaded* code. This script waits for a match, injects the isolated
read-only probe through the existing Windows-message bridge, and stores the
executable section exactly as the previous adaptation rounds did.

It never writes to the game process, never calls a game function and never loads
a driver.
#>
param(
    [int]$GamePid = 0,
    [string]$Output,
    [int]$WaitMinutes = 45,
    [int]$IntervalSeconds = 15,
    [switch]$Rebuild,
    [string]$Python
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $root 'tmp\skin-update-probe'
$hostExe = Join-Path $probe 'bin\league-skin-core-host.exe'
$probeDll = Join-Path $probe 'bin\league-skin-core.dll'

function Resolve-Python {
    if ($Python) { return $Python }
    $candidates = @(
        'C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe',
        "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
        "$env:ProgramFiles\Python313\python.exe",
        "$env:ProgramFiles\Python312\python.exe"
    )
    foreach ($candidate in $candidates) { if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate } }
    $launcher = Get-Command py.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if ($launcher) {
        $probe = & $launcher -3 -c "import sys;print(sys.executable)" 2>$null
        if ($LASTEXITCODE -eq 0 -and $probe -and (Test-Path -LiteralPath $probe)) { return $probe }
    }
    throw '找不到可用的 python.exe，请用 -Python 指定解释器路径。'
}

function Get-GameProcess {
    if ($GamePid -ne 0) { return Get-Process -Id $GamePid -ErrorAction SilentlyContinue }
    Get-Process -Name 'League of Legends' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}

if (-not (Test-Path -LiteralPath $hostExe) -or -not (Test-Path -LiteralPath $probeDll) -or $Rebuild) {
    Write-Host '准备只读探针…'
    $py = Resolve-Python
    & $py (Join-Path $root 'tools\prepare-skin-update-probe.py') | Out-Host
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $probe 'build.ps1') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '探针编译失败' }
}
if (-not (Test-Path -LiteralPath $hostExe)) { throw "探针宿主缺失：$hostExe" }

$deadline = (Get-Date).AddMinutes($WaitMinutes)
$attempt = 0
while ((Get-Date) -lt $deadline) {
    $attempt++
    $game = Get-GameProcess
    if (-not $game) {
        Write-Host ("[{0:HH:mm:ss}] 等待对局进程…" -f (Get-Date))
        Start-Sleep -Seconds $IntervalSeconds
        continue
    }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    if (-not $Output) { $Output = Join-Path $root ".artifacts\skin-update-$stamp" }
    Write-Host ("[{0:HH:mm:ss}] 命中对局 pid={1}，第 {2} 次尝试采集" -f (Get-Date), $game.Id, $attempt)
    $log = Join-Path $root 'tmp\skin-core-capture.log'
    $pythonCode = Join-Path $root 'tools\capture-skin-update-via-bridge.py'
    $result = & (Resolve-Python) $pythonCode --host $hostExe --output $Output 2>&1
    $result | Tee-Object -FilePath $log -Append | Out-Host
    if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $Output 'game-code.bin'))) {
        $manifest = Get-Content (Join-Path $Output 'capture.json') -Raw | ConvertFrom-Json
        Write-Host ("采集完成：{0}（timestamp={1} image_size={2} code={3} 字节 sha256={4}）" -f
            $Output, $manifest.timestamp, $manifest.image_size, $manifest.code_size, $manifest.code_sha256)
        return (Join-Path $Output 'capture.json')
    }
    Write-Host '采集未完成（窗口未就绪/对局已结束/被拒绝），稍后重试。'
    Start-Sleep -Seconds $IntervalSeconds
}
throw "在 $WaitMinutes 分钟内没有完成采集。请在对局内重新运行本脚本。"

#Requires -Version 5.1
<# Run one read-only object-probe request and store the raw reply. #>
param([string]$CoreDir = '', [string]$Output = '')
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path -Parent $PSScriptRoot
if (-not $CoreDir) { $CoreDir = Join-Path $root 'LOL-GameAssistant\Tools\SkinCore' }
if (-not $Output) {
    $latest = Get-ChildItem (Join-Path $root '.artifacts') -Directory -Filter 'skin-update-*' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $Output = Join-Path $latest.FullName 'object-probe.log'
}
$hostExe = Join-Path $CoreDir 'league-skin-core-host.exe'
$raw = & $hostExe object-probe 2>&1 | Out-String
$raw | Set-Content -LiteralPath $Output -Encoding utf8
Write-Host "written: $Output ($($raw.Length) chars)"

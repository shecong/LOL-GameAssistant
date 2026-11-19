#Requires -Version 5.1
<#
Live read/write check of the minion + ward path.

Runs elevated (the game is High integrity). Only object commands are used:
object-catalog (read), objects (apply), objects-restore (restore).
Everything is restored at the end.
#>
param(
    [string]$CoreDir = '',
    [string]$Output = '',
    [int]$MinionIndex = -1,
    [int]$WardSkin = -1,
    [switch]$ReadOnly
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path -Parent $PSScriptRoot
if (-not $CoreDir) { $CoreDir = Join-Path $root 'LOL-GameAssistant\Tools\SkinCore' }
if (-not $Output) {
    $latest = Get-ChildItem (Join-Path $root '.artifacts') -Directory -Filter 'skin-update-*' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $Output = Join-Path $latest.FullName ("live-objects-{0}.json" -f (Get-Date -Format 'HHmmss'))
}
$hostExe = Join-Path $CoreDir 'league-skin-core-host.exe'
$report = [ordered]@{ time = (Get-Date).ToString('o'); steps = @() }
$script:last = $null

function Invoke-Core([string]$label, [string[]]$arguments) {
    $raw = & $hostExe @arguments 2>&1 | Out-String
    $exit = $LASTEXITCODE
    $entry = [ordered]@{ step = $label; exit = $exit }
    try {
        $json = $raw.Substring(0, $raw.LastIndexOf('}') + 1) | ConvertFrom-Json
        $entry.ok = $json.ok
        $entry.error = $json.error
        $entry.stage = $json.stage
        $entry.invoked = $json.invoked
        $entry.state_verified = $json.state_verified
        $entry.targets = $json.object_target_count
        $entry.changed = $json.object_changed_count
        $entry.lanes = $json.lane_count
        $entry.wards = $json.own_ward_count
        $entry.baseline_minion = $json.baseline_minion_skin
        $entry.baseline_ward = $json.baseline_ward_skin
        $entry.session = $json.session
        if ($json.ward_entries) {
            $entry.ward_entry_count = @($json.ward_entries).Count
            $entry.ward_sample = @($json.ward_entries | Select-Object -First 5 | ForEach-Object { "$($_.skin_num):$($_.name)" })
        }
    } catch {
        $entry.parse_error = $_.Exception.Message
        $entry.raw_head = if ($raw.Length -gt 400) { $raw.Substring(0, 400) } else { $raw }
        $json = $null
    }
    $script:report.steps += $entry
    $script:last = $json
    return $entry
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$report.elevated = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

$catalog = Invoke-Core 'object-catalog' @('object-catalog')
if (-not $catalog.ok) {
    $report.failure = "object-catalog failed: $($catalog.error) / $($catalog.stage)"
} else {
    $session = $catalog.session
    $report.catalog = @{ session = $session; lanes = $catalog.lanes; wards = $catalog.wards; baseline_minion = $catalog.baseline_minion; baseline_ward = $catalog.baseline_ward; ward_entries = $catalog.ward_entry_count }
    if (-not $ReadOnly -and $MinionIndex -ge 0 -and $catalog.lanes -gt 0) {
        $apply = Invoke-Core 'objects-apply' @('objects', $session, "$MinionIndex", '-1')
        $report.minion_apply = @{ ok = $apply.ok; error = $apply.error; invoked = $apply.invoked; state_verified = $apply.state_verified; targets = $apply.targets; changed = $apply.changed }
        Start-Sleep -Milliseconds 900
        $readback = Invoke-Core 'object-catalog-readback' @('object-catalog')
        $report.minion_readback = @{ ok = $readback.ok; lanes = $readback.lanes; baseline_minion = $readback.baseline_minion }
        $restore = Invoke-Core 'objects-restore' @('objects-restore', $session, "$($catalog.baseline_minion)", '-1')
        $report.minion_restore = @{ ok = $restore.ok; error = $restore.error; invoked = $restore.invoked; state_verified = $restore.state_verified; targets = $restore.targets; changed = $restore.changed }
        Start-Sleep -Milliseconds 900
        $final = Invoke-Core 'object-catalog-final' @('object-catalog')
        $report.minion_final = @{ ok = $final.ok; lanes = $final.lanes; baseline_minion = $final.baseline_minion }
    }
    if (-not $ReadOnly -and $WardSkin -ge 0 -and $catalog.wards -gt 0) {
        $apply = Invoke-Core 'ward-apply' @('objects', $session, '-1', "$WardSkin")
        $report.ward_apply = @{ ok = $apply.ok; error = $apply.error; invoked = $apply.invoked; state_verified = $apply.state_verified; targets = $apply.targets; changed = $apply.changed }
        Start-Sleep -Milliseconds 900
        $readback = Invoke-Core 'object-catalog-ward-readback' @('object-catalog')
        $report.ward_readback = @{ ok = $readback.ok; wards = $readback.wards; baseline_ward = $readback.baseline_ward }
        $restore = Invoke-Core 'ward-restore' @('objects-restore', $session, '-1', "$($catalog.baseline_ward)")
        $report.ward_restore = @{ ok = $restore.ok; error = $restore.error; invoked = $restore.invoked; state_verified = $restore.state_verified; targets = $restore.targets; changed = $restore.changed }
        Start-Sleep -Milliseconds 900
        $wardFinal = Invoke-Core 'object-catalog-ward-final' @('object-catalog')
        $report.ward_final = @{ ok = $wardFinal.ok; wards = $wardFinal.wards; baseline_ward = $wardFinal.baseline_ward; baseline_minion = $wardFinal.baseline_minion }
    }
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding utf8
Write-Host "written: $Output"

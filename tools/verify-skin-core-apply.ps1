#Requires -Version 5.1
<#
End-to-end check of the skin core inside a running match: catalog, apply one
skin, read the active state back, then restore the original value.

Runs elevated (the game is High integrity). Reversible: the final step restores
the value that was active before the apply.
#>
param(
    [string]$CoreDir = '',
    [string]$Output = '',
    [int]$ApplyIndex = 1
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$root = Split-Path -Parent $PSScriptRoot
if (-not $CoreDir) { $CoreDir = Join-Path $root 'LOL-GameAssistant\Tools\SkinCore' }
if (-not $Output) {
    $latest = Get-ChildItem (Join-Path $root '.artifacts') -Directory -Filter 'skin-update-*' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $Output = Join-Path $latest.FullName 'live-apply.json'
}
$hostExe = Join-Path $CoreDir 'league-skin-core-host.exe'
$report = [ordered]@{ time = (Get-Date).ToString('o'); steps = @() }

function Invoke-Core([string]$label, [string[]]$arguments) {
    $raw = & $hostExe @arguments 2>&1 | Out-String
    $exit = $LASTEXITCODE
    $entry = [ordered]@{ step = $label; exit = $exit }
    try {
        $json = $raw.Substring(0, $raw.LastIndexOf('}') + 1) | ConvertFrom-Json
        $entry.ok = $json.ok
        $entry.error = $json.error
        $entry.invoked = $json.invoked
        $entry.state_verified = $json.state_verified
        $entry.skin = $json.skin
        $entry.active_skin = $json.active_skin
        $entry.active_model = $json.active_model
        $entry.gear = $json.gear
        $entry.session = $json.session
        $entry.model = $json.model
        $entry.entry_count = if ($json.entries) { @($json.entries).Count } else { $null }
        $entry.ids = if ($json.entries) { @($json.entries | Select-Object -First 3 | ForEach-Object { $_.entry_id }) } else { $null }
    } catch {
        $entry.parse_error = $_.Exception.Message
        $entry.raw_head = if ($raw.Length -gt 300) { $raw.Substring(0, 300) } else { $raw }
        $json = $null
    }
    $script:report.steps += $entry
    $script:lastReply = $json
    return $entry
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$report.elevated = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

$catalog = Invoke-Core 'catalog' @('catalog')
if (-not $catalog.ok) {
    $report.failure = "catalog failed: $($catalog.error)"
} else {
    $session = $catalog.session
    $before = $catalog.active_skin
    $report.before = @{ session = $session; skin = $catalog.skin; active_skin = $before; model = $catalog.model; entries = $catalog.entry_count }
    $target = @($script:lastReply.entries | Where-Object { $_.index -eq $ApplyIndex -and $_.gear -eq -1 })[0]
    if (-not $target) {
        $report.failure = "entry $ApplyIndex not present"
    } else {
        $apply = Invoke-Core 'apply' @('entry', $session, "$ApplyIndex", '-1')
        $report.applied = @{ ok = $apply.ok; error = $apply.error; invoked = $apply.invoked; state_verified = $apply.state_verified; skin = $apply.skin; active_skin = $apply.active_skin }
        Start-Sleep -Milliseconds 800
        $readback = Invoke-Core 'catalog-readback' @('catalog')
        $report.after_apply = @{ ok = $readback.ok; active_skin = $readback.active_skin; skin = $readback.skin; model = $readback.model }
        $restore = Invoke-Core 'restore' @('restore', $session)
        $report.restored = @{ ok = $restore.ok; error = $restore.error; invoked = $restore.invoked; state_verified = $restore.state_verified; skin = $restore.skin; active_skin = $restore.active_skin }
        Start-Sleep -Milliseconds 800
        $final = Invoke-Core 'catalog-final' @('catalog')
        $report.after_restore = @{ ok = $final.ok; active_skin = $final.active_skin; skin = $final.skin }
        $report.verdict = if ($apply.state_verified -and $readback.active_skin -eq $ApplyIndex -and $restore.state_verified -and $final.active_skin -eq $before) { 'pass' } else { 'review' }
    }
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Output -Encoding utf8
Write-Host "written: $Output"

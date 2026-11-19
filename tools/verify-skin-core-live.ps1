#Requires -Version 5.1
<#
Live read-only check of the deployed skin core inside a running match.

The game runs at High integrity, so the host must also run elevated. Only the
read-only commands are used: `status`, `profile` and `catalog`.
#>
param(
    [string]$CoreDir = '',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $CoreDir) { $CoreDir = Join-Path $root 'LOL-GameAssistant\Tools\SkinCore' }
if (-not $Output) {
    $latest = Get-ChildItem (Join-Path $root '.artifacts') -Directory -Filter 'skin-update-*' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $Output = Join-Path $latest.FullName 'live-verify.json'
}
$hostExe = Join-Path $CoreDir 'league-skin-core-host.exe'
$result = [ordered]@{ time = (Get-Date).ToString('o'); core = $hostExe; steps = @() }
function Invoke-Step([string]$name, [string[]]$arguments, [switch]$Summarize) {
    $raw = & $hostExe @arguments 2>&1 | Out-String
    $entry = [ordered]@{ command = $name; exit = $LASTEXITCODE; raw_length = $raw.Length }
    try {
        $parsed = $raw | ConvertFrom-Json
        $entry.ok = $parsed.ok
        $entry.error = $parsed.error
        $entry.stage = $parsed.stage
        if ($parsed.profile_resolution) { $entry.profile = $parsed.profile_resolution }
        if ($parsed.player_slot) {
            $entry.player_slot = $parsed.player_slot
            $entry.champion_slot = $parsed.champion_slot
            $entry.translate = $parsed.translate
            $entry.update = $parsed.update
            $entry.push = $parsed.push
            $entry.stack_offset = $parsed.stack_offset
            $entry.skin_offset = $parsed.skin_offset
            $entry.locator_rvas = $parsed.locator_rvas
        }
        if ($Summarize) {
            $entry.model = $parsed.model
            $entry.skin = $parsed.skin
            $entry.gear = $parsed.gear
            $entry.active_skin = $parsed.active_skin
            $entry.active_model = $parsed.active_model
            $entry.entry_count = @($parsed.entries).Count
            $entry.first_entries = @($parsed.entries | Select-Object -First 5 | ForEach-Object {
                if ($_.display_name) { $_.display_name } else { $_.entry_id } })
            $entry.session = $parsed.session
            $entry.reference_sha256 = $parsed.reference_sha256
        }
    } catch {
        $entry.parse_error = $_.Exception.Message
    }
    $script:result.steps += $entry
}
try {
    Invoke-Step 'status' @('status')
    Invoke-Step 'profile' @('profile')
    Invoke-Step 'catalog' @('catalog') -Summarize
} catch {
    $result.failure = $_.Exception.Message
} finally {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $result.elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Output -Encoding utf8
    Write-Host "written: $Output"
}
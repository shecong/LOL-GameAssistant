param([int]$PreviousPid = 27776,[string]$RunName = 'display-v2')
$ErrorActionPreference = 'Stop'
$borderHost = Join-Path $PSScriptRoot '../../LOL-GameAssistant/bin/Debug/net10.0-windows10.0.19041.0/Tools/SkinCore/league-skin-core-host.exe'
$runDirectory=Join-Path $PSScriptRoot $RunName
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$until = [DateTime]::UtcNow.AddMinutes(8)
Write-Output 'Waiting for next training game; CHALLENGER loading verification armed.'
$nextPid = 0
while ([DateTime]::UtcNow -lt $until) {
    $games = @(Get-Process -Name 'League of Legends' -ErrorAction SilentlyContinue)
    try { $nextPid = $games | Where-Object Id -NE $PreviousPid | Select-Object -First 1 -ExpandProperty Id }
    finally { foreach ($game in $games) { $game.Dispose() } }
    if ($nextPid) { break }
    Start-Sleep -Milliseconds 250
}
if (!$nextPid) { Write-Output 'No next game detected.'; exit }
Write-Output "Detected game $nextPid."
$until = [DateTime]::UtcNow.AddSeconds(45)
$sample = 0
while ([DateTime]::UtcNow -lt $until) {
    $raw = (& $borderHost rank-border-auto cn16.21.regalia.v2 CHALLENGER) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $runDirectory ('apply-loading-{0:000}.json' -f $sample)), $raw)
    try { $reply = $raw | ConvertFrom-Json } catch { Write-Output 'Invalid response; stopped without retry.'; exit 1 }
    if ($reply.ok) {
        Write-Output "Application returned: verified=$($reply.state_verified), display=$($reply.rank_border_display_verified), cards=$($reply.rank_border_display_count), tier=$($reply.rank_border_id), session=$($reply.session), baseline=$($reply.rank_border_baseline_id)"
        [IO.File]::WriteAllText((Join-Path $runDirectory 'applied-loading.json'), $raw)
        if ($reply.bridge_protocol -ne 2 -or $reply.rank_border_profile -ne 'cn16.21.regalia.v2' -or !$reply.rank_border_display_verified -or $reply.rank_border_display_count -lt 1) {
            Write-Output 'Stopped: executed version or actual display binding did not pass verification.';exit 1
        }
        $rawState=(& $borderHost rank-border-probe state) -join [Environment]::NewLine
        [IO.File]::WriteAllText((Join-Path $runDirectory 'after-apply-state.json'), $rawState)
        exit
    }
    if ($reply.invoked -or $reply.error -notin @('game_window_not_found', 'rank_border_not_ready', 'rank_border_loading_assets_unavailable')) {
        Write-Output "Stopped without retry: error=$($reply.error), invoked=$($reply.invoked)"; exit 1
    }
    if ($sample % 10 -eq 0) { Write-Output "Waiting: $($reply.error)" }
    $sample++
    Start-Sleep -Milliseconds 250
}
Write-Output 'Loading window expired; stopped.'

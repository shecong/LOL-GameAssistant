param([int]$PreviousPid = 21620)
$ErrorActionPreference = 'Stop'
$probeHost = Join-Path $PSScriptRoot '../../native/LeagueSkinCore/bin/league-skin-core-host.exe'
$watchUntil = [DateTime]::UtcNow.AddMinutes(4)
$nextPid = 0
Write-Output 'Waiting for the next training-game process; read-only capture is armed.'
while ([DateTime]::UtcNow -lt $watchUntil) {
    $gameProcesses = @(Get-Process -Name 'League of Legends' -ErrorAction SilentlyContinue)
    try { $nextPid = ($gameProcesses | Where-Object Id -NE $PreviousPid | Select-Object -First 1 -ExpandProperty Id) }
    finally { foreach ($gameProcess in $gameProcesses) { $gameProcess.Dispose() } }
    if ($nextPid) { break }
    Start-Sleep -Milliseconds 250
}
if (!$nextPid) { Write-Output 'No next game appeared within the capture window.';exit 0 }
Write-Output "New game detected (PID $nextPid); capturing loading-state metadata."
$captureUntil = [DateTime]::UtcNow.AddSeconds(75)
$sample = 0
$catalogObserved = $false
while ([DateTime]::UtcNow -lt $captureUntil) {
    $stateText = (& $probeHost rank-border-probe state) -join [Environment]::NewLine
    $statePath = Join-Path $PSScriptRoot ('loading-state-{0:000}.json' -f $sample)
    [IO.File]::WriteAllText($statePath, $stateText)
    try {
        $state = $stateText | ConvertFrom-Json
        if ($state.ok -and @($state.rank_asset_catalog).Count -gt 0) {
            $catalogObserved = $true
            Write-Output "Ranked-crest catalog captured at sample $sample."
            break
        }
        if ($sample % 10 -eq 0) {
            Write-Output "Sample $sample; ok=$($state.ok); error=$($state.error); local-slot=$($state.local_regalia_slot)"
        }
    } catch { if ($sample % 10 -eq 0) { Write-Output 'Waiting for a complete probe response.' } }
    $sample++
    Start-Sleep -Milliseconds 250
}
Write-Output "Capture finished; samples=$sample; ranked-catalog-observed=$catalogObserved"

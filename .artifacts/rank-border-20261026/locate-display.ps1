$ErrorActionPreference = 'Stop'
$probeHost = Join-Path $PSScriptRoot '../../native/LeagueSkinCore/bin/league-skin-core-host.exe'
$until = [DateTime]::UtcNow.AddMinutes(8)
Write-Output 'Waiting for training game; read-only display investigation.'
while ([DateTime]::UtcNow -lt $until) {
    $games = @(Get-Process -Name 'League of Legends' -ErrorAction SilentlyContinue)
    $ready = $games.Count -eq 1
    foreach ($game in $games) { $game.Dispose() }
    if ($ready) {
        $raw = (& $probeHost rank-border-probe state) -join [Environment]::NewLine
        try { $state = $raw | ConvertFrom-Json } catch { break }
        if ($state.ok) { [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'display-state.json'), $raw); break }
    }
    Start-Sleep -Milliseconds 500
}
if (!$state.ok) { Write-Output 'No readable game available.'; exit }
foreach ($rva in @(0x9edd80, 0x411860, 0x413610, 0x9ea3e0, 0x9ea4d0, 0x9edb60)) {
    $raw = (& $probeHost rank-border-probe $rva) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot ('function-{0:x}.json' -f $rva)), $raw)
    Write-Output ('Captured function {0:x}.' -f $rva)
}

param(
    [string]$HostPath = (Join-Path $PSScriptRoot '..\LOL-GameAssistant\Tools\SkinCore\league-skin-core-host.exe'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\.artifacts\runtime-adaptation'),
    [switch]$TestSkin
)
$ErrorActionPreference = 'Stop'
$autoHost = (Resolve-Path -LiteralPath $HostPath).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$autoCache = [System.IO.MemoryMappedFiles.MemoryMappedFile]::CreateOrOpen('Local\LeagueSkinCore.Auto.ProfileCache.v1', 16384)
try {
    $cold = & $autoHost profile | ConvertFrom-Json
    $warm = & $autoHost profile | ConvertFrom-Json
    if (-not $cold.ok -or -not $warm.ok -or -not $warm.profile_resolution.cache_hit) { throw 'Runtime locator/cache unavailable' }
    $before = & $autoHost catalog | ConvertFrom-Json
    if (-not $before.ok -or -not $before.session) { throw 'Current hero catalog unavailable' }
    $record = [ordered]@{ time_utc=[DateTimeOffset]::UtcNow.ToString('o'); host=$autoHost; original_assistant_running=@(Get-Process -Name 'lol小助手' -ErrorAction SilentlyContinue).Count;
        cold=$cold; warm=$warm; before=$before; write_test_requested=[bool]$TestSkin; visual_confirmation='pending' }
    if ($TestSkin) {
        $stats = Invoke-RestMethod -Uri 'https://127.0.0.1:2999/liveclientdata/gamestats' -SkipCertificateCheck -TimeoutSec 3
        $record.mode=$stats.gameMode; $record.map=$stats.mapName
        $baseline = $before.entries | Where-Object { $_.skin_num -eq $before.active_skin -and $_.model -eq $before.active_model -and $_.gear -eq $before.gear } | Select-Object -First 1
        $target = $before.entries | Where-Object { $_.skin_num -ne $before.active_skin -and $_.model -eq $before.model -and $_.gear -eq -1 } | Select-Object -First 1
        if (-not $baseline -or -not $target) { throw 'No exact restoration baseline; stopped before write' }
        try {
            $applied = & $autoHost entry $before.session $target.index $target.gear | ConvertFrom-Json
            $record.apply=$applied
            if (-not $applied.ok -or -not $applied.state_verified) { throw 'Skin apply unverified' }
            Start-Sleep -Milliseconds 1200
            $after = & $autoHost status | ConvertFrom-Json
            $record.delayed_readback=$after
            if (-not $after.ok -or $after.session -ne $before.session -or $after.active_skin -ne $target.skin_num) { throw 'Delayed readback failed' }
        } finally {
            $current = & $autoHost status | ConvertFrom-Json
            if (-not $current.ok -or $current.session -ne $before.session) { throw 'Session changed; unrelated game will not be restored' }
            $restored = & $autoHost entry $before.session $baseline.index $baseline.gear | ConvertFrom-Json
            $record.restore=$restored
            $final = & $autoHost status | ConvertFrom-Json
            $record.final=$final
            $record | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'live-verification.json') -Encoding utf8
            if (-not $restored.ok -or -not $restored.state_verified -or $final.active_skin -ne $before.active_skin -or $final.gear -ne $before.gear -or $final.active_model -ne $before.active_model) { throw 'Exact baseline restoration unverified' }
        }
    }
    $objects = & $autoHost object-catalog | ConvertFrom-Json
    $camera = & $autoHost camera-status | ConvertFrom-Json
    $record | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'live-verification.json') -Encoding utf8
    @{ objects=$objects; camera=$camera } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'feature-readback.json') -Encoding utf8
    [ordered]@{ mode=$warm.profile_resolution.mode; first_scan_ms=$cold.profile_resolution.elapsed_ms; cached_scan_ms=$warm.profile_resolution.elapsed_ms;
        cache_hit=$warm.profile_resolution.cache_hit; hero=$before.model; baseline=$before.active_skin;
        write_test=[bool]$TestSkin; objects_ok=$objects.ok; camera_ok=$camera.ok; original_running=$record.original_assistant_running } | ConvertTo-Json -Compress
} finally { $autoCache.Dispose() }

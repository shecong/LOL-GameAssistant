param([string]$ArchivePath = '')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$wdkVersion = '10.0.28000.2526'
# SHA512 from the official NuGet catalog entry for this exact package version.
$wdkHash = 'dAGSbkjTr4lFX6hVncyctBDSg87O7x3QSUr8Yf9dFLZ0T3TWuo+i4WrXdviSW77Bdmzn2iu1pA83wGe1V/1akg=='
$wdkCache = Join-Path $repository '.artifacts/wdk-nuget'
New-Item -ItemType Directory -Path $wdkCache -Force | Out-Null
if (-not $ArchivePath) {
    $ArchivePath = Join-Path $wdkCache "microsoft.windows.wdk.x64.$wdkVersion.nupkg"
    if (-not (Test-Path -LiteralPath $ArchivePath)) {
        $partial = "$ArchivePath.partial"
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.wdk.x64/$wdkVersion/microsoft.windows.wdk.x64.$wdkVersion.nupkg" -OutFile $partial
        $expected = [BitConverter]::ToString([Convert]::FromBase64String($wdkHash)).Replace('-', '')
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA512).Hash -ne $expected) { throw 'WDK package SHA512 mismatch.' }
        Move-Item -LiteralPath $partial -Destination $ArchivePath
    }
}
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
$expected = [BitConverter]::ToString([Convert]::FromBase64String($wdkHash)).Replace('-', '')
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA512).Hash -ne $expected) { throw 'WDK package SHA512 mismatch.' }
$destination = Join-Path $wdkCache $wdkVersion
if (-not (Test-Path -LiteralPath $destination)) {
    $staging = Join-Path $wdkCache ("$wdkVersion.staging-" + [Guid]::NewGuid().ToString('N'))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $staging)
    foreach ($required in @('c/Include/10.0.28000.0/km/ntddk.h', 'c/Lib/10.0.28000.0/km/x64/ntoskrnl.lib')) {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Incomplete WDK package: $required" }
    }
    $cachePrefix = [IO.Path]::GetFullPath($wdkCache).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($target in @($staging, $destination)) {
        if (-not [IO.Path]::GetFullPath($target).StartsWith($cachePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'WDK move must stay inside the toolchain cache.'
        }
    }
    Move-Item -LiteralPath $staging -Destination $destination
}
if (-not (Test-Path -LiteralPath (Join-Path $destination 'c/Include/10.0.28000.0/km/ntddk.h'))) { throw 'Prepared WDK is incomplete.' }
Write-Output "WDK $wdkVersion prepared and SHA512 verified: $destination"

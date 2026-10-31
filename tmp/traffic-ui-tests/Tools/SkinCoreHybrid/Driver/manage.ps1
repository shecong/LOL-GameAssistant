#Requires -RunAsAdministrator
param(
    [ValidateSet('Install', 'Uninstall', 'Status')][string]$Action = 'Status',
    [string]$DriverPath = (Join-Path $PSScriptRoot 'SkinCoreRelay.sys')
)
$ErrorActionPreference = 'Stop'
$relayService = Get-CimInstance Win32_SystemDriver -Filter "Name='SkinCoreRelay'" -ErrorAction Stop
if ($Action -eq 'Status') {
    if ($relayService) { $relayService | Select-Object Name, State, StartMode, PathName }
    else { Write-Output 'SkinCoreRelay is not installed.' }
    return
}
if ($Action -eq 'Uninstall') {
    if (-not $relayService) { Write-Output 'SkinCoreRelay is not installed.'; return }
    & sc.exe stop SkinCoreRelay
    if ($LASTEXITCODE -notin @(0, 1062)) { throw 'Driver stop failed. Close relay clients first; no service was deleted.' }
    & sc.exe delete SkinCoreRelay
    if ($LASTEXITCODE -ne 0) { throw 'Driver service deletion failed.' }
    return
}
$relayResolvedPath = (Resolve-Path -LiteralPath $DriverPath).Path
if ([IO.Path]::GetExtension($relayResolvedPath) -ne '.sys') { throw 'Expected a .sys driver file.' }
$relaySignature = Get-AuthenticodeSignature -LiteralPath $relayResolvedPath
if ($relaySignature.Status -ne 'Valid') { throw "Driver signature is not valid ($($relaySignature.Status)). This script does not install unsigned drivers or change Windows security settings." }
if ($relayService) {
    $relayExistingPath = $relayService.PathName.Trim('"')
    if ($relayExistingPath -ne $relayResolvedPath) { throw 'A different SkinCoreRelay service path is already registered. Uninstall the old package before installing this version.' }
} else {
    & sc.exe create SkinCoreRelay type= kernel start= demand binPath= ('"' + $relayResolvedPath + '"')
    if ($LASTEXITCODE -ne 0) { throw 'Driver service registration failed.' }
}
& sc.exe start SkinCoreRelay
if ($LASTEXITCODE -notin @(0, 1056)) { throw 'Driver start was rejected. Inspect Windows Code Integrity/System events; the application will not bypass this failure.' }

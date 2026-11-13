param([string]$WdkRoot = '', [string]$SdkVersion = '10.0.28000.0',
    [string]$KmdfVersion = '', [string]$KmdfMinimumVersion = '1.31')
$ErrorActionPreference = 'Stop'
if (-not $WdkRoot) {
    $relayLocalKit = Join-Path $PSScriptRoot '..\..\.artifacts\wdk-nuget\10.0.28000.2526\c'
    $WdkRoot = if (Test-Path -LiteralPath "$relayLocalKit\Include\$SdkVersion\km\ntddk.h") { (Resolve-Path -LiteralPath $relayLocalKit).Path } else { 'C:\Program Files (x86)\Windows Kits\10' }
}
if (-not (Test-Path -LiteralPath "$WdkRoot\Include\$SdkVersion\km\ntddk.h")) { throw 'WDK kernel headers missing. Supply -WdkRoot pointing to an installed WDK or the c directory of Microsoft.Windows.WDK.x64.' }
$relayVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$relayVs = & $relayVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $relayVs) { throw 'Visual Studio C++ Build Tools missing.' }
$relayVc = (Get-ChildItem -LiteralPath "$relayVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$relaySdk = 'C:\Program Files (x86)\Windows Kits\10'
if (-not (Test-Path -LiteralPath "$relaySdk\Include\$SdkVersion\ucrt\stdio.h") -or
    -not (Test-Path -LiteralPath "$relaySdk\Lib\$SdkVersion\ucrt\x64\ucrt.lib")) {
    throw "Matching Windows SDK $SdkVersion missing. Install Windows SDK 10.0.28000.2957."
}
if (-not $KmdfVersion) {
    $KmdfVersion = (Get-ChildItem -LiteralPath "$WdkRoot\Include\wdf\kmdf" -Directory |
        Where-Object { $_.Name -match '^1\.[0-9]+$' -and (Test-Path -LiteralPath "$WdkRoot\Lib\wdf\kmdf\x64\$($_.Name)\WdfDriverEntry.lib") } |
        Sort-Object { [Version]$_.Name } -Descending | Select-Object -First 1).Name
}
if ($KmdfVersion -notmatch '^1\.[0-9]+$' -or $KmdfMinimumVersion -notmatch '^1\.[0-9]+$') { throw 'Invalid KMDF version.' }
$relayKmdfTarget = [Version]$KmdfVersion
$relayKmdfMinimum = [Version]$KmdfMinimumVersion
if ($relayKmdfMinimum.Minor -lt 25 -or $relayKmdfMinimum -gt $relayKmdfTarget) { throw 'KMDF requires 1.25 <= minimum <= target.' }
foreach ($relayRequired in @("$WdkRoot\Include\wdf\kmdf\$KmdfVersion\wdf.h", "$WdkRoot\Lib\wdf\kmdf\x64\$KmdfVersion\WdfDriverEntry.lib")) {
    if (-not (Test-Path -LiteralPath $relayRequired)) { throw "Missing KMDF component: $relayRequired" }
}
Write-Output "Relay toolchain: MSVC $(Split-Path $relayVc -Leaf); SDK $SdkVersion; WDK $WdkRoot; KMDF $KmdfVersion (minimum $KmdfMinimumVersion)"
$relayOut = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $relayOut -Force | Out-Null
$relayIncludes = @("$relayVc\include", "$WdkRoot\Include\$SdkVersion\km", "$WdkRoot\Include\$SdkVersion\shared", "$relaySdk\Include\$SdkVersion\shared", "$WdkRoot\Include\wdf\kmdf\$KmdfVersion", "$relaySdk\Include\$SdkVersion\ucrt") | ForEach-Object { "/I$_" }
& "$relayVc\bin\Hostx64\x64\cl.exe" /nologo /c /kernel /std:c17 /W4 /WX /O2 /GS /Zl /utf-8 /D_AMD64_ /D_WIN64 /D_WIN32_WINNT=0x0A00 /DNTDDI_VERSION=0x0A000008 /D_NT_TARGET_VERSION=0x0A000008 /DKMDF_VERSION_MAJOR=1 "/DKMDF_VERSION_MINOR=$($relayKmdfTarget.Minor)" "/DKMDF_MINIMUM_VERSION_REQUIRED=$($relayKmdfMinimum.Minor)" @relayIncludes "$PSScriptRoot\driver.c" "/Fo$relayOut\driver.obj"
if ($LASTEXITCODE -ne 0) { throw 'Driver compilation failed.' }
& "$relayVc\bin\Hostx64\x64\link.exe" /nologo /DRIVER /SUBSYSTEM:NATIVE /ENTRY:FxDriverEntry /NODEFAULTLIB /INTEGRITYCHECK /NXCOMPAT /DYNAMICBASE /MACHINE:X64 "/OUT:$relayOut\SkinCoreRelay.sys" "$relayOut\driver.obj" "/LIBPATH:$WdkRoot\Lib\$SdkVersion\km\x64" "/LIBPATH:$WdkRoot\Lib\wdf\kmdf\x64\$KmdfVersion" ntoskrnl.lib hal.lib wdmsec.lib BufferOverflowFastFailK.lib WdfDriverEntry.lib WdfLdr.lib
if ($LASTEXITCODE -ne 0) { throw 'Driver link failed.' }
Write-Output "Built $relayOut\SkinCoreRelay.sys (UNSIGNED; not installed or loaded)."
$relayRuntime = Join-Path $PSScriptRoot '..\..\LOL-GameAssistant\Tools\SkinCoreHybrid\Driver'
New-Item -ItemType Directory -Path $relayRuntime -Force | Out-Null
Copy-Item -LiteralPath "$relayOut\SkinCoreRelay.sys", "$PSScriptRoot\manage.ps1", "$PSScriptRoot\README.md" -Destination $relayRuntime -Force
$relayPackage = Split-Path (Resolve-Path -LiteralPath $relayRuntime).Path -Parent
Get-ChildItem -LiteralPath $relayPackage -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.json' } |
    Get-FileHash -Algorithm SHA256 | Select-Object @{n='file';e={$_.Path.Substring($relayPackage.Length + 1)}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath "$relayPackage\SHA256SUMS.json" -Encoding utf8

param([string]$WdkRoot = '', [string]$SdkVersion = '10.0.26100.0')
$ErrorActionPreference = 'Stop'
if (-not $WdkRoot) {
    $relayLocalKit = Join-Path $PSScriptRoot '..\..\.artifacts\wdk-nuget\wdk\c'
    $WdkRoot = if (Test-Path -LiteralPath "$relayLocalKit\Include\$SdkVersion\km\ntddk.h") { (Resolve-Path -LiteralPath $relayLocalKit).Path } else { 'C:\Program Files (x86)\Windows Kits\10' }
}
if (-not (Test-Path -LiteralPath "$WdkRoot\Include\$SdkVersion\km\ntddk.h")) { throw 'WDK kernel headers missing. Supply -WdkRoot pointing to an installed WDK or the c directory of Microsoft.Windows.WDK.x64.' }
$relayVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$relayVs = & $relayVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $relayVs) { throw 'Visual Studio C++ Build Tools missing.' }
$relayVc = (Get-ChildItem -LiteralPath "$relayVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$relaySdk = 'C:\Program Files (x86)\Windows Kits\10'
$relayOut = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $relayOut -Force | Out-Null
$relayIncludes = @("$relayVc\include", "$WdkRoot\Include\$SdkVersion\km", "$WdkRoot\Include\$SdkVersion\shared", "$relaySdk\Include\$SdkVersion\shared", "$WdkRoot\Include\wdf\kmdf\1.31", "$relaySdk\Include\$SdkVersion\ucrt") | ForEach-Object { "/I$_" }
& "$relayVc\bin\Hostx64\x64\cl.exe" /nologo /c /kernel /std:c17 /W4 /WX /O2 /GS /Zl /utf-8 /D_AMD64_ /D_WIN64 /D_WIN32_WINNT=0x0A00 /DNTDDI_VERSION=0x0A000008 @relayIncludes "$PSScriptRoot\driver.c" "/Fo$relayOut\driver.obj"
if ($LASTEXITCODE -ne 0) { throw 'Driver compilation failed.' }
& "$relayVc\bin\Hostx64\x64\link.exe" /nologo /DRIVER /SUBSYSTEM:NATIVE /ENTRY:FxDriverEntry /NODEFAULTLIB /INTEGRITYCHECK /NXCOMPAT /DYNAMICBASE /MACHINE:X64 "/OUT:$relayOut\SkinCoreRelay.sys" "$relayOut\driver.obj" "/LIBPATH:$WdkRoot\Lib\$SdkVersion\km\x64" "/LIBPATH:$WdkRoot\Lib\wdf\kmdf\x64\1.31" ntoskrnl.lib hal.lib wdmsec.lib BufferOverflowFastFailK.lib WdfDriverEntry.lib WdfLdr.lib
if ($LASTEXITCODE -ne 0) { throw 'Driver link failed.' }
Write-Output "Built $relayOut\SkinCoreRelay.sys (UNSIGNED; not installed or loaded)."
$relayRuntime = Join-Path $PSScriptRoot '..\..\LOL-GameAssistant\Tools\SkinCoreHybrid\Driver'
New-Item -ItemType Directory -Path $relayRuntime -Force | Out-Null
Copy-Item -LiteralPath "$relayOut\SkinCoreRelay.sys", "$PSScriptRoot\manage.ps1", "$PSScriptRoot\README.md" -Destination $relayRuntime -Force
$relayPackage = Split-Path (Resolve-Path -LiteralPath $relayRuntime).Path -Parent
Get-ChildItem -LiteralPath $relayPackage -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.json' } |
    Get-FileHash -Algorithm SHA256 | Select-Object @{n='file';e={$_.Path.Substring($relayPackage.Length + 1)}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath "$relayPackage\SHA256SUMS.json" -Encoding utf8

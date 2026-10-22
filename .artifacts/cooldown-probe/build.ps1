param([string]$Revision = 'v2')
$ErrorActionPreference = 'Stop'
$probeVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$probeVs = & $probeVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $probeVs) { throw 'C++ Build Tools missing.' }
$probeVc = (Get-ChildItem -LiteralPath "$probeVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$probeSdk = 'C:\Program Files (x86)\Windows Kits\10'
$probeSdkVersion = (Get-ChildItem -LiteralPath "$probeSdk\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\x64\kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:PATH = "$probeVc\bin\Hostx64\x64;$env:PATH"
$env:LIB = "$probeVc\lib\x64;$probeSdk\Lib\$probeSdkVersion\ucrt\x64;$probeSdk\Lib\$probeSdkVersion\um\x64"
$env:INCLUDE = "$probeVc\include;$probeSdk\Include\$probeSdkVersion\ucrt;$probeSdk\Include\$probeSdkVersion\shared;$probeSdk\Include\$probeSdkVersion\um"
$probeOutput = Join-Path $PSScriptRoot $Revision
New-Item -ItemType Directory -Path $probeOutput -Force | Out-Null
Push-Location $probeOutput
try {
    & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /LD "$PSScriptRoot/probe.cpp" /Fe:cooldown-readonly-probe.dll /link user32.lib bcrypt.lib
    if ($LASTEXITCODE -ne 0) { throw 'Probe DLL compilation failed.' }
    & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /DBUILD_HOST "$PSScriptRoot/probe.cpp" /Fe:cooldown-readonly-probe.exe /link user32.lib bcrypt.lib
    if ($LASTEXITCODE -ne 0) { throw 'Probe host compilation failed.' }
} finally { Pop-Location }

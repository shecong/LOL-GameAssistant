param([switch]$Test)
$ErrorActionPreference = 'Stop'
$updaterVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $updaterVsWhere)) { throw 'Install Visual Studio C++ Build Tools (Desktop development with C++) and Windows SDK.' }
$updaterVs = & $updaterVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $updaterVs) { throw 'Visual Studio C++ x64 tools not found.' }
$updaterVc = (Get-ChildItem -LiteralPath "$updaterVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$updaterSdk = "${env:ProgramFiles(x86)}\Windows Kits\10"
$updaterSdkVersion = (Get-ChildItem -LiteralPath "$updaterSdk\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\x64\kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
if (-not $updaterSdkVersion) { throw 'Windows SDK not found.' }
$updaterOriginalPath = $env:PATH
$updaterOriginalLib = $env:LIB
$updaterOriginalInclude = $env:INCLUDE
$updaterOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $updaterOutput -Force | Out-Null
try {
    $env:PATH = "$updaterVc\bin\Hostx64\x64;$env:PATH"
    $env:LIB = "$updaterVc\lib\x64;$updaterSdk\Lib\$updaterSdkVersion\ucrt\x64;$updaterSdk\Lib\$updaterSdkVersion\um\x64"
    $env:INCLUDE = "$updaterVc\include;$updaterSdk\Include\$updaterSdkVersion\ucrt;$updaterSdk\Include\$updaterSdkVersion\shared;$updaterSdk\Include\$updaterSdkVersion\um"
    Push-Location $updaterOutput
    try {
        if ($Test) {
            & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /W4 "$PSScriptRoot\updater_tests.cpp" "/Fe:$updaterOutput\updater-tests.exe" /link user32.lib shell32.lib bcrypt.lib comctl32.lib
            if ($LASTEXITCODE -ne 0) { throw 'Updater test compilation failed.' }
            & "$updaterOutput\updater-tests.exe"
            if ($LASTEXITCODE -ne 0) { throw 'Updater tests failed.' }
        } else {
            & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /W4 /DUNICODE /D_UNICODE "$PSScriptRoot\updater.cpp" "/Fe:$updaterOutput\Updater.exe" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib shell32.lib bcrypt.lib comctl32.lib "/MANIFESTUAC:level='requireAdministrator' uiAccess='false'"
            if ($LASTEXITCODE -ne 0) { throw 'Updater compilation failed.' }
            $updaterRepository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
            $updaterDestination = Join-Path $updaterRepository 'LOL-GameAssistant\Tools\Updater'
            New-Item -ItemType Directory -Path $updaterDestination -Force | Out-Null
            Copy-Item -LiteralPath "$updaterOutput\Updater.exe" -Destination $updaterDestination
            Get-Item -LiteralPath "$updaterOutput\Updater.exe" | Select-Object FullName, Length
        }
    } finally { Pop-Location }
} finally {
    $env:PATH = $updaterOriginalPath
    $env:LIB = $updaterOriginalLib
    $env:INCLUDE = $updaterOriginalInclude
}

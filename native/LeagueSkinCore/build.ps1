param([switch]$Test)
$ErrorActionPreference = 'Stop'
$coreVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$coreVs = & $coreVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $coreVs) { throw 'Install Visual Studio C++ Build Tools and Windows SDK first.' }
$coreVc = (Get-ChildItem -LiteralPath "$coreVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$coreSdk = 'C:\Program Files (x86)\Windows Kits\10'
$coreSdkVersion = (Get-ChildItem -LiteralPath "$coreSdk\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\x64\kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:PATH = "$coreVc\bin\Hostx64\x64;$env:PATH"
$env:LIB = "$coreVc\lib\x64;$coreSdk\Lib\$coreSdkVersion\ucrt\x64;$coreSdk\Lib\$coreSdkVersion\um\x64"
$env:INCLUDE = "$coreVc\include;$coreSdk\Include\$coreSdkVersion\ucrt;$coreSdk\Include\$coreSdkVersion\shared;$coreSdk\Include\$coreSdkVersion\um"
$coreOut = "$PSScriptRoot\bin"
New-Item -ItemType Directory -Path $coreOut -Force | Out-Null
Push-Location "$PSScriptRoot\native"
try {
    if ($Test) {
        & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 core_tests.cpp "/Fe:$coreOut\core-tests.exe" /link user32.lib bcrypt.lib
        if ($LASTEXITCODE -ne 0) { throw 'Core tests compilation failed' }
        & "$coreOut\core-tests.exe"
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
    } else {
        & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /LD core.cpp "/Fe:$coreOut\league-skin-core.dll" /link user32.lib bcrypt.lib
        if ($LASTEXITCODE -ne 0) { throw 'Core DLL compilation failed' }
        & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /DBUILD_HOST core.cpp "/Fe:$coreOut\league-skin-core-host.exe" /link user32.lib bcrypt.lib
        if ($LASTEXITCODE -ne 0) { throw 'Core host compilation failed' }
    }
} finally { Pop-Location }

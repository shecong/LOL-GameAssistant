param([switch]$Test)
$ErrorActionPreference = 'Stop'
$hybridVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$hybridVs = & $hybridVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $hybridVs) { throw 'Install Visual Studio C++ Build Tools and Windows SDK first.' }
$hybridVc = (Get-ChildItem -LiteralPath "$hybridVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$hybridSdk = 'C:\Program Files (x86)\Windows Kits\10'
$hybridVersion = (Get-ChildItem -LiteralPath "$hybridSdk\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\x64\kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:PATH = "$hybridVc\bin\Hostx64\x64;$env:PATH"
$env:LIB = "$hybridVc\lib\x64;$hybridSdk\Lib\$hybridVersion\ucrt\x64;$hybridSdk\Lib\$hybridVersion\um\x64"
$env:INCLUDE = "$hybridVc\include;$hybridSdk\Include\$hybridVersion\ucrt;$hybridSdk\Include\$hybridVersion\shared;$hybridSdk\Include\$hybridVersion\um"
$hybridOut = "$PSScriptRoot\bin"
New-Item -ItemType Directory -Path $hybridOut -Force | Out-Null
Push-Location "$PSScriptRoot\native"
try {
    & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /LD hybrid.cpp "/Fe:$hybridOut\league-skin-core.dll" /link user32.lib bcrypt.lib
    if ($LASTEXITCODE -ne 0) { throw 'Hybrid DLL compilation failed' }
    & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 /DBUILD_HOST hybrid.cpp "/Fe:$hybridOut\league-skin-hybrid-host.exe" /link user32.lib bcrypt.lib
    if ($LASTEXITCODE -ne 0) { throw 'Hybrid host compilation failed' }
    & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 "$PSScriptRoot\..\SkinCoreRelay\test_client.cpp" "/Fe:$hybridOut\relay-test-client.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Relay test client compilation failed' }
    if ($Test) {
        & cl.exe /nologo /utf-8 /O2 /MT /std:c17 "$PSScriptRoot\..\SkinCoreRelay\tests\dispatch_tests.c" "/Fe:$hybridOut\relay-dispatch-tests.exe"
        if ($LASTEXITCODE -ne 0) { throw 'Relay dispatch test compilation failed' }
        & "$hybridOut\relay-dispatch-tests.exe"
        if ($LASTEXITCODE -ne 0) { throw 'Relay dispatch tests failed' }
        & "$hybridOut\relay-test-client.exe" --protocol-only
        if ($LASTEXITCODE -ne 0) { throw 'Relay protocol tests failed' }
    }
} finally { Pop-Location }
$hybridRuntime = Join-Path $PSScriptRoot '..\..\LOL-GameAssistant\Tools\SkinCoreHybrid'
New-Item -ItemType Directory -Path $hybridRuntime -Force | Out-Null
Copy-Item -LiteralPath "$hybridOut\league-skin-core.dll", "$hybridOut\league-skin-hybrid-host.exe" -Destination $hybridRuntime -Force
Copy-Item -LiteralPath "$PSScriptRoot\..\LeagueSkinCore\licenses" -Destination $hybridRuntime -Recurse -Force
$hybridRuntime = (Resolve-Path -LiteralPath $hybridRuntime).Path
Get-ChildItem -LiteralPath $hybridRuntime -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.json' } |
    Get-FileHash -Algorithm SHA256 | Select-Object @{n='file';e={$_.Path.Substring($hybridRuntime.Length + 1)}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath "$hybridRuntime\SHA256SUMS.json" -Encoding utf8

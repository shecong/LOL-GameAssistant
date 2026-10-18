param([switch]$Test)
$ErrorActionPreference = 'Stop'
$overlayPins = @{
    imgui = @('https://github.com/ocornut/imgui.git', 'v1.91.9b', 'f5befd2d29e66809cd1110a152e375a7f1981f06')
    minhook = @('https://github.com/TsudaKageyu/minhook.git', 'v1.3.4', 'c3fcafdc10146beb5919319d0683e44e3c30d537')
}
foreach ($overlayDependency in $overlayPins.Keys) {
    $overlayPath = Join-Path $PSScriptRoot "vendor/$overlayDependency"
    $overlayPin = $overlayPins[$overlayDependency]
    if (-not (Test-Path -LiteralPath $overlayPath)) {
        & git clone --depth 1 --branch $overlayPin[1] $overlayPin[0] $overlayPath
        if ($LASTEXITCODE -ne 0) { throw "Failed to fetch $overlayDependency" }
    }
    $overlayCommit = & git -C $overlayPath rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $overlayCommit -ne $overlayPin[2]) { throw "Unexpected $overlayDependency revision" }
    $overlayDirty = & git -C $overlayPath status --porcelain
    if ($overlayDirty) { throw "Modified $overlayDependency sources" }
}
$overlayVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$overlayVs = & $overlayVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $overlayVs) { throw 'Install Visual Studio C++ Build Tools and Windows SDK.' }
$overlayVc = (Get-ChildItem -LiteralPath "$overlayVs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$overlaySdk = 'C:\Program Files (x86)\Windows Kits\10'
$overlaySdkVersion = (Get-ChildItem -LiteralPath "$overlaySdk\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\x64\d3d11.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:PATH = "$overlayVc\bin\Hostx64\x64;$env:PATH"
$env:LIB = "$overlayVc\lib\x64;$overlaySdk\Lib\$overlaySdkVersion\ucrt\x64;$overlaySdk\Lib\$overlaySdkVersion\um\x64"
$env:INCLUDE = "$overlayVc\include;$overlaySdk\Include\$overlaySdkVersion\ucrt;$overlaySdk\Include\$overlaySdkVersion\shared;$overlaySdk\Include\$overlaySdkVersion\um;$overlaySdk\Include\$overlaySdkVersion\winrt"
$overlayOutput = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $overlayOutput -Force | Out-Null
Push-Location $overlayOutput
try {
    $overlayMinhook = "$PSScriptRoot/vendor/minhook"
    & cl.exe /nologo /O2 /MT /c /TC "/I$overlayMinhook/include" "$overlayMinhook/src/buffer.c" "$overlayMinhook/src/hook.c" "$overlayMinhook/src/trampoline.c" "$overlayMinhook/src/hde/hde64.c"
    if ($LASTEXITCODE -ne 0) { throw 'MinHook compilation failed' }
    $overlayImgui = "$PSScriptRoot/vendor/imgui"
    $overlayCommon = @('/nologo','/utf-8','/O2','/MT','/EHsc','/std:c++20',"/I$overlayImgui", "/I$overlayImgui/backends", "/I$overlayMinhook/include")
    $overlaySources = @("$PSScriptRoot/overlay.cpp", "$overlayImgui/imgui.cpp", "$overlayImgui/imgui_draw.cpp", "$overlayImgui/imgui_tables.cpp", "$overlayImgui/imgui_widgets.cpp", "$overlayImgui/backends/imgui_impl_dx11.cpp", 'buffer.obj', 'hook.obj', 'trampoline.obj', 'hde64.obj')
    & cl.exe @overlayCommon /LD @overlaySources /Fe:sc-game-overlay.dll /link user32.lib d3d11.lib dxgi.lib d3dcompiler.lib
    if ($LASTEXITCODE -ne 0) { throw 'Overlay compilation failed' }
    & cl.exe @overlayCommon "$PSScriptRoot/host.cpp" /Fe:sc-game-overlay-host.exe /link user32.lib
    if ($LASTEXITCODE -ne 0) { throw 'Overlay host compilation failed' }
    if ($Test) {
        & cl.exe @overlayCommon /LD /DSC_OVERLAY_TEST @overlaySources /Fe:sc-game-overlay-test.dll /link user32.lib d3d11.lib dxgi.lib d3dcompiler.lib
        if ($LASTEXITCODE -ne 0) { throw 'Test overlay compilation failed' }
        & cl.exe @overlayCommon "$PSScriptRoot/smoke.cpp" /Fe:sc-game-overlay-smoke.exe /link user32.lib d3d11.lib dxgi.lib d3dcompiler.lib
        if ($LASTEXITCODE -ne 0) { throw 'Smoke harness compilation failed' }
        & ./sc-game-overlay-smoke.exe
        if ($LASTEXITCODE -ne 0) { throw 'Overlay render smoke test failed' }
    }
    $overlayDestination = Join-Path $PSScriptRoot '../../LOL-GameAssistant/Tools/GameOverlay'
    New-Item -ItemType Directory -Path $overlayDestination -Force | Out-Null
    Copy-Item -LiteralPath './sc-game-overlay.dll','./sc-game-overlay-host.exe' -Destination $overlayDestination -Force
    Copy-Item -LiteralPath "$overlayImgui/LICENSE.txt" -Destination "$overlayDestination/ImGui-LICENSE.txt" -Force
    Copy-Item -LiteralPath "$overlayMinhook/LICENSE.txt" -Destination "$overlayDestination/MinHook-LICENSE.txt" -Force
} finally { Pop-Location }

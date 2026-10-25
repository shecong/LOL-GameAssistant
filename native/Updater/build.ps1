param([switch]$Test, [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string]$Runtime = 'win-x64', [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$updaterArch = $Runtime.Substring(4)
$updaterVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $updaterVsWhere)) { throw 'Install Visual Studio C++ Build Tools (Desktop development with C++) and Windows SDK.' }
$updaterInstallations = @(& $updaterVsWhere -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
if ($LASTEXITCODE -ne 0 -or $updaterInstallations.Count -eq 0) { throw 'Visual Studio C++ build tools not found.' }
# 最新 IDE 不一定安装了目标架构；同时检查其他 VS/Build Tools 和并存的 MSVC 版本。
$updaterVc = $null
foreach ($updaterInstallation in $updaterInstallations) {
    $updaterToolsRoot = Join-Path $updaterInstallation 'VC\Tools\MSVC'
    if (-not (Test-Path -LiteralPath $updaterToolsRoot -PathType Container)) { continue }
    foreach ($updaterToolset in (Get-ChildItem -LiteralPath $updaterToolsRoot -Directory | Sort-Object { [Version]$_.Name } -Descending)) {
        if ((Test-Path -LiteralPath "$($updaterToolset.FullName)\bin\Hostx64\$updaterArch\cl.exe") -and
            (Test-Path -LiteralPath "$($updaterToolset.FullName)\lib\$updaterArch\libcmt.lib")) {
            $updaterVc = $updaterToolset.FullName
            break
        }
    }
    if ($updaterVc) { break }
}
if (-not $updaterVc) {
    throw "缺少 MSVC $updaterArch 编译器，不能生成 $Runtime 更新器。打开 Visual Studio Installer → 修改 → 单个组件，安装对应的 MSVC $updaterArch 编译工具后重试。"
}
Write-Host "原生更新器工具链：$Runtime -> $updaterVc"
$updaterSdk = "${env:ProgramFiles(x86)}\Windows Kits\10"
$updaterSdkVersion = (Get-ChildItem -LiteralPath "$updaterSdk\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\$updaterArch\kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
if (-not $updaterSdkVersion) { throw 'Windows SDK not found.' }
foreach ($updaterRequiredLibrary in @("$updaterVc\lib\$updaterArch\libcmt.lib", "$updaterSdk\Lib\$updaterSdkVersion\ucrt\$updaterArch\ucrt.lib")) {
    if (-not (Test-Path -LiteralPath $updaterRequiredLibrary)) { throw "缺少 $Runtime 编译库：$updaterRequiredLibrary，请在 Visual Studio Installer 中补齐对应工具和 Windows SDK。" }
}
if ($CheckOnly) {
    Write-Host "原生更新器构建环境检查通过：$Runtime"
    return
}
$updaterOriginalPath = $env:PATH
$updaterOriginalLib = $env:LIB
$updaterOriginalInclude = $env:INCLUDE
$updaterOutput = Join-Path $PSScriptRoot "bin\$Runtime"
New-Item -ItemType Directory -Path $updaterOutput -Force | Out-Null
try {
    $env:PATH = "$updaterVc\bin\Hostx64\$updaterArch;$env:PATH"
    $env:LIB = "$updaterVc\lib\$updaterArch;$updaterSdk\Lib\$updaterSdkVersion\ucrt\$updaterArch;$updaterSdk\Lib\$updaterSdkVersion\um\$updaterArch"
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
            $updaterDestination = Join-Path $updaterRepository "LOL-GameAssistant\Tools\Updater\$Runtime"
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

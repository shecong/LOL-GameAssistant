[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$CompilerPath,
    [string]$ChineseMessagesPath,
    [string]$OutputDirectory,
    [string]$UpdaterPath,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'UpdatePublishing.ps1')
$tools = Join-Path $repo '.artifacts/installer-tools'
$project = Join-Path $repo 'LOL-GameAssistant/LOL-GameGUI.csproj'
$runtimeArch = $Runtime.ToLowerInvariant().Substring(4)
$Runtime = "win-$runtimeArch"
$entryName = Get-UpdateEntryExecutable -Repository $repo
[xml]$versionConfig = Get-Content -LiteralPath (Join-Path $repo 'build/Version.props') -Raw
$version = [string]$versionConfig.Project.PropertyGroup.SoftwareVersion
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Invalid SoftwareVersion in build/Version.props.'
}

if (-not $CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @(
        (Join-Path $tools 'inno-portable/tools/ISCC.exe'),
        "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
        "$env:ProgramFiles/Inno Setup 7/ISCC.exe",
        $command.Source
    )
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Inno Setup compiler not found. Supply -CompilerPath with the path to ISCC.exe.'
}
$CompilerPath = (Resolve-Path -LiteralPath $CompilerPath).Path
$runtimeConfig = Join-Path $repo "installer/runtime-$runtimeArch.json"
$runtimePackage = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
$runtimeUri = [Uri]$runtimePackage.url
if ($runtimePackage.version -notmatch '^10\.0\.[0-9]+$' -or $runtimePackage.sha256 -notmatch '^[0-9a-f]{64}$' -or
    -not $runtimeUri.IsAbsoluteUri -or $runtimeUri.Scheme -ne 'https' -or
    $runtimeUri.Host -notin @('download.microsoft.com', 'builds.dotnet.microsoft.com', 'dotnetcli.blob.core.windows.net')) {
    throw "Invalid Microsoft Desktop Runtime configuration in $runtimeConfig."
}
$runtimeInstaller = "windowsdesktop-runtime-$($runtimePackage.version)-$Runtime.exe"
if ([IO.Path]::GetFileName($runtimeUri.AbsolutePath) -ne $runtimeInstaller) {
    throw "Desktop Runtime URL does not match the configured $Runtime version."
}
if ($CheckOnly) { return }

if (-not $ChineseMessagesPath) {
    $ChineseMessagesPath = Join-Path (Split-Path $CompilerPath -Parent) 'Languages/ChineseSimplified.isl'
    if (-not (Test-Path -LiteralPath $ChineseMessagesPath -PathType Leaf)) {
        $ChineseMessagesPath = Join-Path $tools 'ChineseSimplified.isl'
        if (-not (Test-Path -LiteralPath $ChineseMessagesPath -PathType Leaf)) {
            New-Item -ItemType Directory -Path $tools -Force | Out-Null
            Invoke-WebRequest 'https://raw.githubusercontent.com/jrsoftware/issrc/refs/heads/main/Files/Languages/ChineseSimplified.isl' -OutFile $ChineseMessagesPath
        }
    }
}
$ChineseMessagesPath = (Resolve-Path -LiteralPath $ChineseMessagesPath).Path

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo ".artifacts/installers/$version/$Runtime" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$payload = Join-Path $repo ('.artifacts/installers/work/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null

$updater = Resolve-UpdateUpdater -Repository $repo -Runtime $Runtime -UpdaterPath $UpdaterPath
& dotnet publish $project -c Release -r $Runtime --self-contained false `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
$entry = Join-Path $payload $entryName
if ((Get-UpdateExecutableRuntime -Path $entry) -ne $Runtime) { throw "Expected a $Runtime application executable." }
Assert-UpdateSkinDependencies -Repository $repo -PublishDirectory $payload
Copy-Item -LiteralPath $updater -Destination (Join-Path $payload 'Updater.exe') -Force

foreach ($relative in @('Data/Skins/skins.plskin', 'Data/Skins/skinlib', 'Tools/Cslol/hashes.game.txt', 'settings.json', '.update', 'cache', 'logs', 'userdata')) {
    if (Test-Path -LiteralPath (Join-Path $payload $relative)) { throw "Unexpected installer payload: $relative" }
}
foreach ($relative in @('Tools/SkinCore/licenses/LICENSE', 'Tools/SkinCoreHybrid/licenses/LICENSE')) {
    $source = Join-Path (Join-Path $repo 'LOL-GameAssistant') $relative
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        $destination = Join-Path $payload $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}
$hashLines = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/')
    $digest = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest`t$relative"
})
[IO.File]::WriteAllLines((Join-Path $payload 'update-files.txt'), [string[]]$hashLines, [Text.UTF8Encoding]::new($false))

$compilerArgs = @(
    "/DAppVersion=$version",
    "/DAppExe=$entryName",
    "/DPayloadDir=$payload",
    "/DOutputDirectory=$output",
    "/DChineseMessages=$ChineseMessagesPath",
    "/DRuntimeArch=$runtimeArch",
    "/DRuntimeUrl=$($runtimePackage.url)",
    "/DRuntimeInstaller=$runtimeInstaller",
    "/DRuntimeSha256=$($runtimePackage.sha256)",
    "/DRuntimeMinimumPatch=$(([Version]$runtimePackage.version).Build)",
    (Join-Path $repo 'installer/sc-assistant.iss')
)
& $CompilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$installer = Join-Path $output "sc-assistant-setup-$version-$Runtime.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$installer.sha256", "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Installer: $installer"
Write-Host "SHA256: $hash"

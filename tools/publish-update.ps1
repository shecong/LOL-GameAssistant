[CmdletBinding()]
param(
    [string]$Version,
    [string]$Tag,
    [string]$OutputDirectory,
    [string]$PublishDirectory,
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$ServerBaseUrl,
    [string]$ReleaseNotesFile,
    [switch]$AllowHttpServer,
    [switch]$RebuildUpdater,
    [string]$UpdaterPath,
    [switch]$FlatOutput,
    [switch]$SkipUpdaterBuild
)
$ErrorActionPreference = 'Stop'
if ($ServerBaseUrl) {
    $updateServerUri = [Uri]$ServerBaseUrl
    if (-not $updateServerUri.IsAbsoluteUri -or ($updateServerUri.Scheme -ne 'https' -and -not ($AllowHttpServer -and $updateServerUri.Scheme -eq 'http')) -or $updateServerUri.UserInfo -or $updateServerUri.Query -or $updateServerUri.Fragment) {
        throw 'ServerBaseUrl must be an absolute HTTPS URL (or HTTP with -AllowHttpServer), without credentials, query or fragment.'
    }
}
$updateRepository = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'UpdatePublishing.ps1')
$updateEntryName = Get-UpdateEntryExecutable -Repository $updateRepository
if ($RebuildUpdater -and ($SkipUpdaterBuild -or $UpdaterPath)) { throw '-RebuildUpdater cannot be combined with -SkipUpdaterBuild or -UpdaterPath.' }
$updateVersionConfigPath = Join-Path $updateRepository 'build\Version.props'
[xml]$updateVersionConfig = Get-Content -LiteralPath $updateVersionConfigPath -Raw
$updateConfiguredVersion = [string]$updateVersionConfig.Project.PropertyGroup.SoftwareVersion
if ($updateConfiguredVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Set SoftwareVersion in build/Version.props to a numeric three-part version.'
}
if ($Version -and $Version -ne $updateConfiguredVersion) {
    throw "Requested Version differs from build/Version.props ($updateConfiguredVersion). Edit the configuration and republish first."
}
$Version = $updateConfiguredVersion
$updateIndex = New-UpdateIndexJson -Version $Version -ReleaseNotesFile $ReleaseNotesFile
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be a numeric three-part version, for example 6.1.0.' }
$updateVersion = [Version]::Parse($Version)
if (-not $Tag) { $Tag = "v$Version" }
$updateTagVersion = [Version]::Parse($Tag.TrimStart('v', 'V'))
if ($updateTagVersion.Major -ne $updateVersion.Major -or $updateTagVersion.Minor -ne $updateVersion.Minor -or [Math]::Max(0, $updateTagVersion.Build) -ne $updateVersion.Build -or [Math]::Max(0, $updateTagVersion.Revision) -ne 0) {
    throw 'Tag and package Version must match.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $updateRepository ".artifacts\updates\$Version\$Runtime" }
$updateOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $FlatOutput -and (Test-Path -LiteralPath $updateOutput)) { throw "Output directory already exists. Choose a new empty path: $updateOutput" }
if ($FlatOutput) {
    foreach ($updateExistingAsset in @("LOL-GameAssistant-$Runtime.zip", "update-$Runtime.json")) {
        if (Test-Path -LiteralPath (Join-Path $updateOutput $updateExistingAsset)) { throw "该架构发布文件已存在，不能覆盖：$updateExistingAsset。请升级版本或更换输出根目录。" }
    }
}
if ($RebuildUpdater) {
    & (Join-Path $updateRepository 'native\Updater\build.ps1') -Runtime $Runtime
}
$updateUpdater = Resolve-UpdateUpdater -Repository $updateRepository -Runtime $Runtime -PublishDirectory $PublishDirectory -UpdaterPath $UpdaterPath
Write-Host "复用原生更新器：$updateUpdater"
New-Item -ItemType Directory -Path $updateOutput -Force | Out-Null
$updateWork = if ($FlatOutput) {
    Join-Path $updateRepository (".artifacts\updates\pack-work\$Version\$Runtime\" + [Guid]::NewGuid().ToString('N'))
} else { Join-Path $updateOutput 'work' }
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $updateWork 'publish'
    & dotnet publish (Join-Path $updateRepository 'LOL-GameAssistant\LOL-GameGUI.csproj') -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -o $PublishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
}
$updateSource = [IO.Path]::GetFullPath($PublishDirectory)
$updateEntry = Join-Path $updateSource $updateEntryName
if (-not (Test-Path -LiteralPath $updateEntry)) { throw "Publish folder is missing $updateEntryName. 请重新发布改名后的主程序，不要手动重命名旧版 EXE。" }
if ((Get-UpdateExecutableRuntime -Path $updateEntry) -ne $Runtime) { throw "Executable architecture does not match $Runtime : $updateEntry" }
$updateFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($updateEntry)
if ($updateFileVersion.FileMajorPart -ne $updateVersion.Major -or $updateFileVersion.FileMinorPart -ne $updateVersion.Minor -or $updateFileVersion.FileBuildPart -ne $updateVersion.Build -or $updateFileVersion.FilePrivatePart -ne 0) {
    throw 'Published executable version does not match build/Version.props. Publish the application again after changing the configuration.'
}
$updateStage = Join-Path $updateWork 'package'
New-Item -ItemType Directory -Path $updateStage | Out-Null
foreach ($updateItem in Get-ChildItem -LiteralPath $updateSource -Recurse -Force) {
    if ($updateItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish folders cannot contain reparse points.' }
}
foreach ($updateFile in Get-ChildItem -LiteralPath $updateSource -Recurse -File) {
    if ($updateFile.Extension -eq '.pdb' -or $updateFile.Name -eq 'update-files.txt') { continue }
    $updateRelative = [IO.Path]::GetRelativePath($updateSource, $updateFile.FullName)
    $updateFirst = ($updateRelative -split '[\\/]')[0]
    if ($updateFirst -in @('.update', 'logs', 'cache', 'userdata') -or $updateFile.Name -eq 'settings.json') {
        throw "User data must not be included in publish folder: $updateRelative"
    }
    $updateDestination = Join-Path $updateStage $updateRelative
    New-Item -ItemType Directory -Path (Split-Path $updateDestination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $updateFile.FullName -Destination $updateDestination
}
Copy-Item -LiteralPath $updateUpdater -Destination (Join-Path $updateStage 'Updater.exe') -Force
if ($ServerBaseUrl) {
    $updateSources = @{ serverBaseUrl = $ServerBaseUrl.TrimEnd('/') + '/'; allowHttp = [bool]$AllowHttpServer } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $updateStage 'update-source.json'), $updateSources, [Text.UTF8Encoding]::new($false))
}
$updateLines = @(Get-ChildItem -LiteralPath $updateStage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $updateRelative = [IO.Path]::GetRelativePath($updateStage, $_.FullName).Replace('\', '/')
    if ($updateRelative.Length -gt 220 -or $updateRelative.Contains("`t") -or $updateRelative.Contains("`n")) { throw 'Invalid package path.' }
    $updateHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$updateHash`t$updateRelative"
})
[IO.File]::WriteAllLines((Join-Path $updateStage 'update-files.txt'), [string[]]$updateLines, [Text.UTF8Encoding]::new($false))
$updateZipName = "LOL-GameAssistant-$Runtime.zip"
$updateVersionFolder = if ($FlatOutput) { $updateOutput } else { Join-Path $updateOutput "v$Version\$Runtime" }
New-Item -ItemType Directory -Path $updateVersionFolder -Force | Out-Null
$updateZip = Join-Path $updateVersionFolder $updateZipName
[IO.Compression.ZipFile]::CreateFromDirectory($updateStage, $updateZip, [IO.Compression.CompressionLevel]::Optimal, $false)
$updateManifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    runtime = $Runtime
    package = $updateZipName
    size = (Get-Item -LiteralPath $updateZip).Length
    sha256 = (Get-FileHash -LiteralPath $updateZip -Algorithm SHA256).Hash.ToLowerInvariant()
    entryExecutable = $updateEntryName
}
$updateJson = $updateManifest | ConvertTo-Json
$updateVersionManifest = if ($FlatOutput) { Join-Path $updateOutput "update-$Runtime.json" } else { Join-Path $updateVersionFolder 'update.json' }
$updateGithubManifest = Join-Path $updateOutput "update-$Runtime.json"
[IO.File]::WriteAllText($updateVersionManifest, $updateJson, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($updateGithubManifest, $updateJson, [Text.UTF8Encoding]::new($false))
if ($Runtime -eq 'win-x64' -and -not $FlatOutput) {
    $updateLegacyFolder = Join-Path $updateOutput 'github-legacy'
    New-Item -ItemType Directory -Path $updateLegacyFolder -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $updateLegacyFolder 'update.json'), $updateJson, [Text.UTF8Encoding]::new($false))
}
if (-not $FlatOutput) {
    [IO.File]::WriteAllText((Join-Path $updateOutput 'update.json'), $updateIndex, [Text.UTF8Encoding]::new($false))
}
Write-Host "IIS: ZIP and full manifest belong in v$Version/$Runtime/ (manifest name on IIS: update.json). Publish the root index last."
Write-Host "GitHub $Tag assets: $updateZipName and update-$Runtime.json (repeat per architecture)."
$updateResultPaths = @($updateZip, $updateVersionManifest, $updateGithubManifest) | Select-Object -Unique
if (-not $FlatOutput) { $updateResultPaths += Join-Path $updateOutput 'update.json' }
Get-Item -LiteralPath $updateResultPaths | Select-Object FullName, Length

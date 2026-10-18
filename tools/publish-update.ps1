[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Tag,
    [string]$OutputDirectory,
    [string]$PublishDirectory,
    [string]$ServerBaseUrl,
    [switch]$AllowHttpServer,
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
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be a numeric three-part version, for example 6.1.0.' }
$updateVersion = [Version]::Parse($Version)
if (-not $Tag) { $Tag = "v$Version" }
$updateTagVersion = [Version]::Parse($Tag.TrimStart('v', 'V'))
if ($updateTagVersion.Major -ne $updateVersion.Major -or $updateTagVersion.Minor -ne $updateVersion.Minor -or [Math]::Max(0, $updateTagVersion.Build) -ne $updateVersion.Build -or [Math]::Max(0, $updateTagVersion.Revision) -ne 0) {
    throw 'Tag and package Version must match.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $updateRepository ".artifacts\updates\$Version" }
$updateOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $updateOutput) { throw "Output directory already exists. Choose a new empty path: $updateOutput" }
if (-not $SkipUpdaterBuild) {
    & (Join-Path $updateRepository 'native\Updater\build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Native updater build failed.' }
}
$updateUpdater = Join-Path $updateRepository 'LOL-GameAssistant\Tools\Updater\Updater.exe'
if (-not (Test-Path -LiteralPath $updateUpdater)) { throw 'Build native/Updater first.' }
New-Item -ItemType Directory -Path $updateOutput | Out-Null
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $updateOutput 'publish'
    & dotnet publish (Join-Path $updateRepository 'LOL-GameAssistant\LOL-GameGUI.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" -o $PublishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
}
$updateSource = [IO.Path]::GetFullPath($PublishDirectory)
$updateEntry = Join-Path $updateSource 'LOL-GameGUI.exe'
if (-not (Test-Path -LiteralPath $updateEntry)) { throw 'Publish folder is missing LOL-GameGUI.exe.' }
$updateFileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($updateEntry)
if ($updateFileVersion.FileMajorPart -ne $updateVersion.Major -or $updateFileVersion.FileMinorPart -ne $updateVersion.Minor -or $updateFileVersion.FileBuildPart -ne $updateVersion.Build -or $updateFileVersion.FilePrivatePart -ne 0) {
    throw 'Published executable version does not match requested Version. Publish with -p:Version=<Version>.'
}
$updateStage = Join-Path $updateOutput 'package'
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
$updateZipName = 'LOL-GameAssistant-win-x64.zip'
$updateZip = Join-Path $updateOutput $updateZipName
[IO.Compression.ZipFile]::CreateFromDirectory($updateStage, $updateZip, [IO.Compression.CompressionLevel]::Optimal, $false)
$updateManifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    runtime = 'win-x64'
    package = $updateZipName
    size = (Get-Item -LiteralPath $updateZip).Length
    sha256 = (Get-FileHash -LiteralPath $updateZip -Algorithm SHA256).Hash.ToLowerInvariant()
    entryExecutable = 'LOL-GameGUI.exe'
}
[IO.File]::WriteAllText((Join-Path $updateOutput 'update.json'), ($updateManifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Host "Created release assets for tag $Tag. Upload BOTH files to the same GitHub Release:"
Get-Item -LiteralPath $updateZip, (Join-Path $updateOutput 'update.json') | Select-Object FullName, Length

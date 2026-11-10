[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$OutputDirectory,
    [string]$ServerBaseUrl = 'http://159.75.148.241/',
    [string]$Tag = 'file-skin-resources-26.1109'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $PSScriptRoot '../LOL-GameAssistant' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '../.artifacts/file-skin-resources' }
$source = [IO.Path]::GetFullPath($SourceDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$archivePath = Join-Path $output 'file-skin-resources.zip'
if (Test-Path -LiteralPath $archivePath) { throw '资源包已存在，请使用新的输出目录，避免覆盖已发布资源。' }
$paths = @('Data/Skins/skins.plskin', 'Data/Skins/manifest.json', 'Tools/Cslol/hashes.game.txt',
    'Tools/Cslol/mod-tools.exe', 'Tools/Cslol/cslol-dll.dll', 'Tools/Cslol/ritobin_cli.exe',
    'Tools/Cslol/wad-extract.exe', 'Tools/Cslol/wad-make.exe', 'Tools/Cslol/README.md', 'Tools/Cslol/SHA256SUMS.json')
$index = [ordered]@{}
foreach ($relative in $paths) {
    $path = Join-Path $source $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "缺少资源：$relative" }
    $index[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
$zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $paths) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $source $relative), $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $entry = $zip.CreateEntry('resource-files.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($index | ConvertTo-Json)) } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
$digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$configuration = @{ urls = @(($ServerBaseUrl.TrimEnd('/') + '/resources/' + $Tag + '/file-skin-resources.zip'),
    "https://github.com/shecong/LOL-GameAssistant/releases/download/$Tag/file-skin-resources.zip"); sha256 = $digest }
[IO.File]::WriteAllText((Join-Path $output 'file-skin-resources.json'), ($configuration | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Host "资源包：$archivePath"
Write-Host "SHA256：$digest"
Write-Host '发布到服务器的 resources/<Tag>/ 目录及 GitHub 同名 Release 后，将 file-skin-resources.json 的内容放入 update-source.json 的 fileSkinResources 字段。'

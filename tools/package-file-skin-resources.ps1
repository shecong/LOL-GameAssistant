[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$OutputDirectory,
    [string]$ServerBaseUrl = 'http://159.75.148.241/',
    [string]$Tag,
    [string]$ResourceVersion = (Get-Date -Format 'yy.MMdd'),
    [string]$GamePatch = '',
    [string]$ReleaseNotes = '更新游戏外换肤资源。',
    [switch]$IncludeServerMirror
)
$ErrorActionPreference = 'Stop'
if ($ResourceVersion -notmatch '^\d+(\.\d+){1,3}$') { throw '资源版本必须为数字点分格式，例如 26.1115.1' }
if (-not $Tag) { $Tag = 'file-skin-resources-' + $ResourceVersion }
if ($Tag -notmatch '^[A-Za-z0-9._-]+$') { throw '资源标签包含无效字符' }
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
if (-not $GamePatch) {
    $catalog = Get-Content -LiteralPath (Join-Path $source 'Data/Skins/manifest.json') -Raw | ConvertFrom-Json
    if ($catalog.patch) { $GamePatch = [string]$catalog.patch }
}
$versionBytes = [Text.Encoding]::UTF8.GetBytes((@{ version = $ResourceVersion; gamePatch = $GamePatch } | ConvertTo-Json))
$shaTool = [Security.Cryptography.SHA256]::Create()
try { $versionHash = [BitConverter]::ToString($shaTool.ComputeHash($versionBytes)).Replace('-', '').ToLowerInvariant() }
finally { $shaTool.Dispose() }
$index = [ordered]@{ 'resource-version.json' = $versionHash }
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
    $versionEntry = $zip.CreateEntry('resource-version.json')
    $versionStream = $versionEntry.Open()
    try { $versionStream.Write($versionBytes, 0, $versionBytes.Length) } finally { $versionStream.Dispose() }
    $entry = $zip.CreateEntry('resource-files.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(($index | ConvertTo-Json)) } finally { $writer.Dispose() }
} finally { $zip.Dispose() }
$digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$urls = @("https://github.com/shecong/LOL-GameAssistant/releases/download/$Tag/file-skin-resources.zip")
if ($IncludeServerMirror) { $urls = @(($ServerBaseUrl.TrimEnd('/') + '/resources/' + $Tag + '/file-skin-resources.zip')) + $urls }
$configuration = @{ version = $ResourceVersion; gamePatch = $GamePatch; urls = $urls; sha256 = $digest;
    size = (Get-Item -LiteralPath $archivePath).Length; notes = $ReleaseNotes }
[IO.File]::WriteAllText((Join-Path $output 'file-skin-resources.json'), ($configuration | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
Write-Host "资源包：$archivePath"
Write-Host "SHA256：$digest"
Write-Host '先发布不可变 ZIP，再将 file-skin-resources.json 更新到仓库 build/file-skin-resources.json（或配置的稳定清单地址）。无需重新发布主程序。'
Write-Host '仅在服务器镜像确认可访问后使用 -IncludeServerMirror。资源 Release 不要设为主程序的最新发行版。'

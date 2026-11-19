#Requires -Version 5.1
<#
Run the compiled native resolver against a captured game image.

This answers the question the assistant cannot answer without a match: do the
rules that are actually compiled into the core locate the captured build? The
snapshot is laid out exactly like a loaded image (calling code at its real RVA,
everything else zero-filled), so `runtimeResolve` runs its normal section scan,
relative-target checks and layout invariants.

Exit code 0 means the compiled rules resolved the build; the printed values are
the ones the resolver would use in-process.
#>
param(
    [Parameter(Mandatory = $true)][string]$Capture,
    [string]$Tests = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$captureDir = (Resolve-Path -LiteralPath $Capture).Path
$code = Join-Path $captureDir 'game-code.bin'
$manifestPath = Join-Path $captureDir 'capture.json'
if (-not (Test-Path -LiteralPath $code)) { throw "缺少 game-code.bin：$code" }
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "缺少 capture.json：$manifestPath" }
if (-not $Tests) { $Tests = Join-Path $root 'native\LeagueSkinCore\bin\runtime-profile-tests.exe' }
if (-not (Test-Path -LiteralPath $Tests)) { throw "缺少已编译的测试程序：$Tests（先运行 native\LeagueSkinCore\build.ps1 -Test）" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$imagePath = Join-Path $captureDir 'runtime-code.bin'
if (-not (Test-Path -LiteralPath $imagePath)) {
    Write-Host "按 capture.json 还原映像布局：rva=$($manifest.code_rva) size=$($manifest.image_size)"
    $bytes = [System.IO.File]::ReadAllBytes($code)
    $image = New-Object byte[] ([int]$manifest.image_size)
    [Array]::Copy($bytes, 0, $image, [int]$manifest.code_rva, [Math]::Min($bytes.Length, $image.Length - [int]$manifest.code_rva))
    [System.IO.File]::WriteAllBytes($imagePath, $image)
}
Write-Host "对局构建：timestamp=$($manifest.timestamp) image_size=$($manifest.image_size) code_sha256=$($manifest.code_sha256)"
& $Tests $imagePath
$exit = $LASTEXITCODE
if ($exit -eq 0) {
    Write-Host '结论：当前编译进核心的规则可以定位该构建。'
} else {
    Write-Host '结论：当前规则无法定位该构建，需要用 tools\report-skin-runtime-capture.py 看具体失败的定位键。'
}
exit $exit

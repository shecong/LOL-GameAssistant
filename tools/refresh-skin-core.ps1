#Requires -Version 5.1
<#
Rebuild the skin core after a rules/profile change and validate it against a capture.

Order matters: regenerate the compiled rules, run the native regression tests,
rebuild and deploy both cores, then let the *compiled* resolver locate the
captured build. Step 4 is the acceptance gate short of a live match: it fails
with the real `profile_signature_missing` / `profile_signature_ambiguous` /
`profile_schema_not_verified` reason when the rules are still wrong.

Example:
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\refresh-skin-core.ps1 `
      -Capture .artifacts\skin-update-20261119
#>
param(
    [string]$Capture = '',
    [ValidateSet('Classic', 'Hybrid', 'Both')][string]$Project = 'Both',
    [switch]$SkipGenerate,
    [string]$Python = '',
    [string]$PythonPath = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Resolve-Python {
    if ($Python) { return $Python }
    $candidates = @(
        'C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe',
        "$env:LOCALAPPDATA\Programs\Python\Python313\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe"
    )
    foreach ($candidate in $candidates) { if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate } }
    throw '找不到可用的 python.exe，请用 -Python 指定解释器路径。'
}

function Test-PythonModule([string]$interpreter, [string]$module) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $null = & $interpreter -c "import $module" 2>&1
        return ($LASTEXITCODE -eq 0)
    } finally { $ErrorActionPreference = $previous }
}

function Ensure-Capstone([string]$interpreter) {
    if (Test-PythonModule $interpreter 'capstone') { return }
    $local = Join-Path $root '.artifacts\rank-border-20261026\python-deps'
    if (Test-Path -LiteralPath (Join-Path $local 'capstone')) {
        $env:PYTHONPATH = if ($env:PYTHONPATH) { "$local;$env:PYTHONPATH" } else { $local }
        if (Test-PythonModule $interpreter 'capstone') { return }
    }
    throw '缺少 capstone：请 pip install capstone，或用 -PythonPath 指向包含 capstone 的目录。'
}

$interpreter = Resolve-Python
if ($PythonPath) { $env:PYTHONPATH = if ($env:PYTHONPATH) { "$PythonPath;$env:PYTHONPATH" } else { $PythonPath } }
$env:PYTHONIOENCODING = 'utf-8'

if (-not $SkipGenerate) {
    Write-Host '1/4 重新生成定位规则…'
    Ensure-Capstone $interpreter
    & $interpreter (Join-Path $root 'tools\generate-skin-runtime-patterns.py')
    if ($LASTEXITCODE -ne 0) { throw '规则生成失败' }
} else {
    Write-Host '1/4 跳过规则生成'
}

Write-Host '2/4 原生回归测试…'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'native\LeagueSkinCore\build.ps1') -Test
if ($LASTEXITCODE -ne 0) { throw '原生测试失败' }

Write-Host '3/4 编译并部署核心…'
foreach ($name in $(if ($Project -eq 'Both') { @('Classic', 'Hybrid') } else { @($Project) })) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'native\Build-SkinCoreProject.ps1') -Project $name
    if ($LASTEXITCODE -ne 0) { throw "$name 核心编译失败" }
}
if ($Project -ne 'Classic') {
    $hybridSource = Join-Path $root 'native\LeagueSkinHybrid\bin'
    $hybridTarget = Join-Path $root 'LOL-GameAssistant\Tools\SkinCoreHybrid'
    Copy-Item -LiteralPath (Join-Path $hybridSource 'league-skin-core.dll'), (Join-Path $hybridSource 'league-skin-hybrid-host.exe') -Destination $hybridTarget -Force
    $sums = Get-Content -LiteralPath (Join-Path $hybridTarget 'SHA256SUMS.json') -Raw | ConvertFrom-Json
    foreach ($row in $sums) {
        if ($row.file -in @('league-skin-core.dll', 'league-skin-hybrid-host.exe')) {
            $row.Hash = (Get-FileHash -LiteralPath (Join-Path $hybridTarget $row.file) -Algorithm SHA256).Hash
        }
    }
    $sums | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $hybridTarget 'SHA256SUMS.json') -Encoding utf8
}

if ($Capture) {
    Write-Host '4/4 用编译后的核心解析采集快照…'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\validate-skin-runtime-capture.ps1') -Capture $Capture
    if ($LASTEXITCODE -ne 0) { throw '编译后的规则仍然无法定位该构建，请先用 tools\report-skin-runtime-capture.py 查看失败的定位键。' }
} else {
    Write-Host '4/4 未提供 -Capture，跳过对采集快照的验收'
}
Write-Host '完成。'

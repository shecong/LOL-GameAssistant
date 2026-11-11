#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssetDirectory,
    [string]$OutputDirectory,
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')][string[]]$Runtime = @('win-x64', 'win-x86', 'win-arm64'),
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'UpdatePublishing.ps1')
$serverOutput = Export-UpdateServerLayout @PSBoundParameters
Write-Host "服务器成品目录：$serverOutput"
Write-Host '复用已有 ZIP 和安装 EXE，未重新构建或压缩。将此目录内的 v版本 目录复制到服务器网站根目录，最后覆盖根目录 update.json。'

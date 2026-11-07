param([Parameter(Mandatory = $true)][string]$Source, [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$expectedHash = 'DA1B89FB914992D012AEBF5557C41274497463A7A5D2CB8ECFACEE51F872A3E4'
$sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
if ($sourceHash -notin @($expectedHash, 'E4FBBF9671303712A764D505D9E9A6C5FAE7004873899255AB798F759D23E69B')) { throw 'HID.dll SHA256 does not match a supported user-supplied loader.' }
$componentDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'LOL-GameAssistant\Tools\ResourceSkins'
New-Item -ItemType Directory -Path $componentDir -Force | Out-Null
$destination = Join-Path $componentDir 'HID.dll'
if ($sourceHash -eq $expectedHash) {
    if ([IO.Path]::GetFullPath($Source) -ne [IO.Path]::GetFullPath($destination)) { Copy-Item -LiteralPath $Source -Destination $destination -Force }
} else {
    & $Python (Join-Path $PSScriptRoot 'prepare-resource-hid.py') $Source $destination
    if ($LASTEXITCODE -ne 0) { throw 'Compatible HID loader generation failed (requires Python pefile).' }
}
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Compatible HID loader verification failed.' }
Write-Output "Prepared verified resource loader: $componentDir"

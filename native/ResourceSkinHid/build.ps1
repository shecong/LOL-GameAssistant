param([string]$OutputDirectory = "$PSScriptRoot\bin")
$ErrorActionPreference = 'Stop'
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsInstall = & $vsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vsInstall) { throw 'Visual Studio C++ tools are required' }
$vcTools = (Get-ChildItem -LiteralPath "$vsInstall\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdkRoot = 'C:\Program Files (x86)\Windows Kits\10'
$sdkVersion = (Get-ChildItem -LiteralPath "$sdkRoot\Lib" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\x64\kernel32.lib" } | Sort-Object Name -Descending | Select-Object -First 1).Name
Write-Output "Resource HID toolchain: MSVC $(Split-Path $vcTools -Leaf); Windows SDK $sdkVersion"
$env:PATH = "$vcTools\bin\Hostx64\x64;$env:PATH"
$env:LIB = "$vcTools\lib\x64;$sdkRoot\Lib\$sdkVersion\ucrt\x64;$sdkRoot\Lib\$sdkVersion\um\x64"
$env:INCLUDE = "$vcTools\include;$sdkRoot\Include\$sdkVersion\ucrt;$sdkRoot\Include\$sdkVersion\shared;$sdkRoot\Include\$sdkVersion\um"
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$exports = Get-Content (Join-Path $PSScriptRoot '../../tools/resource-hid-exports.json') -Raw | ConvertFrom-Json
$assembly = [Collections.Generic.List[string]]::new()
$definition = [Collections.Generic.List[string]]::new()
$names = [Collections.Generic.List[string]]::new()
$assembly.Add('EXTERN ResolveHid:PROC'); $assembly.Add('.code')
$definition.Add('LIBRARY HID'); $definition.Add('EXPORTS')
$names.Add('static const char* exportNames[] = {')
for ($index = 0; $index -lt $exports.Count; $index++) {
    $entry = $exports[$index]
    $names.Add('"' + $entry.name + '",')
    $definition.Add("$($entry.name)=HidProxy_$index @$($entry.ordinal)")
    $assembly.Add(@"
HidProxy_$index PROC
 sub rsp, 0A8h
 mov [rsp+20h], rcx
 mov [rsp+28h], rdx
 mov [rsp+30h], r8
 mov [rsp+38h], r9
 movdqu [rsp+40h], xmm0
 movdqu [rsp+50h], xmm1
 movdqu [rsp+60h], xmm2
 movdqu [rsp+70h], xmm3
 mov ecx, $index
 call ResolveHid
 mov rcx, [rsp+20h]
 mov rdx, [rsp+28h]
 mov r8, [rsp+30h]
 mov r9, [rsp+38h]
 movdqu xmm0, [rsp+40h]
 movdqu xmm1, [rsp+50h]
 movdqu xmm2, [rsp+60h]
 movdqu xmm3, [rsp+70h]
 add rsp, 0A8h
 test rax, rax
 jz missing_$index
 jmp rax
missing_$index`:
 xor eax, eax
 ret
HidProxy_$index ENDP
"@)
}
$assembly.Add('END'); $names.Add('};')
[IO.File]::WriteAllLines((Join-Path $outputPath 'thunks.asm'), $assembly)
[IO.File]::WriteAllLines((Join-Path $outputPath 'HID.def'), $definition)
[IO.File]::WriteAllLines((Join-Path $outputPath 'exports.inc'), $names)
Push-Location $outputPath
try {
    & ml64.exe /nologo /c /Fothunks.obj thunks.asm
    if ($LASTEXITCODE) { throw 'HID thunks assembly failed' }
    & cl.exe /nologo /utf-8 /O2 /MT /EHsc /std:c++20 "/I$outputPath" /LD "$PSScriptRoot/proxy.cpp" thunks.obj /Fe:HID.dll /link /DEF:HID.def /Brepro kernel32.lib
    if ($LASTEXITCODE) { throw 'HID proxy build failed' }
} finally { Pop-Location }

$ErrorActionPreference='Stop'
$vswhere="${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vc=(Get-ChildItem -LiteralPath "$vs\VC\Tools\MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk='C:\Program Files (x86)\Windows Kits\10'
$ver=(Get-ChildItem -LiteralPath "$sdk\Lib" -Directory | Sort-Object Name -Descending | Select-Object -First 1).Name
$env:LIB="$vc\lib\x64;$sdk\Lib\$ver\ucrt\x64;$sdk\Lib\$ver\um\x64"
$env:INCLUDE="$vc\include;$sdk\Include\$ver\ucrt;$sdk\Include\$ver\shared;$sdk\Include\$ver\um;$sdk\Include\$ver\winrt"
& "$vc\bin\Hostx64\x64\cl.exe" /nologo /utf-8 /EHsc audio_probe.cpp /Fe:audio_probe.exe /link ole32.lib user32.lib propsys.lib
if($LASTEXITCODE -ne 0){throw 'Build failed'}
& ./audio_probe.exe


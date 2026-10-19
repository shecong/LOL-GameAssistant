@echo off
setlocal DisableDelayedExpansion
set "UPDATE_PWSH="
for /f "delims=" %%P in ('where pwsh.exe 2^>nul') do if not defined UPDATE_PWSH set "UPDATE_PWSH=%%P"
if not defined UPDATE_PWSH if exist "%ProgramFiles%\PowerShell\7\pwsh.exe" set "UPDATE_PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not defined UPDATE_PWSH if exist "%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe" set "UPDATE_PWSH=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe"
if not defined UPDATE_PWSH goto missing_powershell
if "%~1"=="" goto interactive
"%UPDATE_PWSH%" -NoLogo -NoProfile -File "%~dp0tools\publish-update-menu.ps1" %*
exit /b %errorlevel%

:interactive
"%UPDATE_PWSH%" -NoLogo -NoProfile -File "%~dp0tools\publish-update-menu.ps1"
set "UPDATE_EXIT=%errorlevel%"
echo.
pause
exit /b %UPDATE_EXIT%

:missing_powershell
echo PowerShell 7 was not found. Install PowerShell 7 or add pwsh.exe to PATH.
if "%~1"=="" pause
exit /b 1

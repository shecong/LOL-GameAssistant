@echo off
rem Right-click -> Run as administrator. Captures the running match (read-only),
rem prints the locator report and validates the snapshot with the compiled core.
setlocal
set "ROOT=%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0capture-and-analyze-skin-runtime.ps1" -WaitMinutes 20 -IntervalSeconds 5 -Pause
endlocal

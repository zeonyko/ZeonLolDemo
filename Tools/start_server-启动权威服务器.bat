@echo off
REM Thin launcher. Logic lives in start_server.ps1 (UTF-8).
REM Keep this .bat ASCII-only. cmd parses .bat as system ANSI.
cd /d "%~dp0"
title ZeonLolDemo Server
echo.
echo [Start] build + run authoritative server
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_server.ps1"
set ERR=%ERRORLEVEL%
echo.
if /i not "%~1"=="nopause" pause
exit /b %ERR%

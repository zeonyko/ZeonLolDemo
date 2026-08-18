@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo.
echo [Init] link Shared + check Server
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0init.ps1"
set ERR=%ERRORLEVEL%
echo.
if /i not "%~1"=="nopause" pause
exit /b %ERR%

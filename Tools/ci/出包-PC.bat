@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo.
echo [CI] 出安装包  Win64
echo Keep this window open. Close Unity Editor for this project first.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-unity.ps1" -Platform Win64 -Task BuildPlayer
set ERR=%ERRORLEVEL%
echo.
if /i not "%~1"=="nopause" pause
exit /b %ERR%

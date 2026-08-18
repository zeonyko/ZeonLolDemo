@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
echo.
echo [CI] 发布补丁  Android
echo Keep this window open. Close Unity Editor for this project first.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-unity.ps1" -Platform Android -Task PublishPatch
set ERR=%ERRORLEVEL%
echo.
if /i not "%~1"=="nopause" pause
exit /b %ERR%

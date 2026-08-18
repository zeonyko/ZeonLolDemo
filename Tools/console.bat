@echo off
REM ------------------------------------------------------------
REM console.bat — ZeonLolDemo tools menu (ASCII name, safe to double-click)
REM Also available as 控制台.bat
REM ------------------------------------------------------------
chcp 65001 >nul
cd /d "%~dp0"
title ZeonLolDemo Tools

if not exist "%~dp0Console.ps1" (
  echo [ERROR] Console.ps1 not found
  echo         %~dp0Console.ps1
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Console.ps1"
set ERR=%ERRORLEVEL%
if not "%ERR%"=="0" (
  echo.
  echo [FAIL] Console exited with code %ERR%
  pause
)
exit /b %ERR%

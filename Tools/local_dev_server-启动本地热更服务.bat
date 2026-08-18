@echo off
REM ASCII-only launcher. Logic in local_dev_server.js
cd /d "%~dp0"

where node >nul 2>nul
if errorlevel 1 (
  echo Node.js not found. Install Node.js and add it to PATH.
  if /i not "%~1"=="nopause" pause
  exit /b 1
)

echo Local VersionCheck + CDN
echo Config: Tools\local_dev.json
echo Ctrl+C to stop.
echo.

if /i "%~1"=="nopause" (
  node "%~dp0local_dev_server.js"
) else (
  node "%~dp0local_dev_server.js" %*
)
set ERR=%ERRORLEVEL%
if errorlevel 1 if /i not "%~1"=="nopause" pause
exit /b %ERR%

@echo off
REM ------------------------------------------------------------
REM sync_config-同步配置表.bat
REM
REM 从仓库权威源 Config/ 按 config.sync.json 同步到：
REM   Client\Assets\Game\Configs
REM   Server\Config
REM
REM 策略见 Config\config.sync.json（copy / skip / logicOnly）。
REM 改表或导出技能后跑一次；不必开 Unity。
REM ------------------------------------------------------------
chcp 65001 >nul
setlocal
cd /d "%~dp0.."
title ZeonLolDemo Sync Config

set "ROOT=%CD%"
set "PS1=%ROOT%\Tools\SyncConfig.ps1"

echo.
if not exist "%PS1%" (
    echo [ERROR] 找不到 Tools\SyncConfig.ps1
    goto :FAIL
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
if errorlevel 1 goto :FAIL

if /i not "%~1"=="nopause" (
    echo.
    pause
)
endlocal
exit /b 0

:FAIL
echo.
echo [FAIL] 配置同步未完成
if /i not "%~1"=="nopause" (
    echo.
    pause
)
endlocal
exit /b 1

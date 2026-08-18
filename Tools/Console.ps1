# ZeonLolDemo Tools Console
# Encoding: UTF-8 BOM
$ErrorActionPreference = "Continue"

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Resolve-Path (Join-Path $ToolsDir "..")

function Pause-Enter {
    Write-Host ""
    [void](Read-Host "按 Enter 返回菜单")
}

function Invoke-Bat([string]$relativePath) {
    $path = Join-Path $ToolsDir $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "[ERROR] 找不到: $relativePath" -ForegroundColor Red
        Pause-Enter
        return
    }

    Write-Host ""
    Write-Host ">>> $relativePath" -ForegroundColor Cyan
    Write-Host ""

    Push-Location (Split-Path $path -Parent)
    try {
        & cmd.exe /c "`"$path`" nopause"
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    if ($code -ne 0) {
        Write-Host "[FAIL] exit=$code" -ForegroundColor Red
    }
    else {
        Write-Host "[OK] 完成" -ForegroundColor Green
    }
    Pause-Enter
}

function Show-Header([string]$subtitle = "") {
    Clear-Host
    Write-Host "========================================"
    Write-Host "  ZeonLolDemo 工具"
    Write-Host "========================================"
    Write-Host "  仓库: $Root"
    if ($subtitle) {
        Write-Host "  $subtitle"
    }
    Write-Host ""
}

try {
    while ($true) {
        Show-Header
        Write-Host "  日常"
        Write-Host "  [1] 初始化（Shared 联接 + 本机 IP）"
        Write-Host "  [2] 同步配置表"
        Write-Host "  [3] 启动游戏服"
        Write-Host "  [4] 启动热更服务"
        Write-Host ""
        Write-Host "  出包（请先关掉 Unity；编辑器里也可用 Launch 菜单）"
        Write-Host "  [5] 发布补丁 PC"
        Write-Host "  [6] 发布补丁 Android"
        Write-Host "  [7] 出安装包 PC"
        Write-Host "  [8] 出安装包 Android"
        Write-Host ""
        Write-Host "  [0] 退出"
        Write-Host ""
        $choice = Read-Host "选择"
        switch ($choice) {
            "1" { Invoke-Bat "init-初始化.bat" }
            "2" { Invoke-Bat "sync_config-同步配置表.bat" }
            "3" { Invoke-Bat "start_server-启动权威服务器.bat" }
            "4" { Invoke-Bat "local_dev_server-启动本地热更服务.bat" }
            "5" { Invoke-Bat "ci\发布补丁-PC.bat" }
            "6" { Invoke-Bat "ci\发布补丁-Android.bat" }
            "7" { Invoke-Bat "ci\出包-PC.bat" }
            "8" { Invoke-Bat "ci\出包-Android.bat" }
            "0" { exit 0 }
            default { Write-Host "无效选项"; Start-Sleep -Seconds 1 }
        }
    }
}
catch {
    Write-Host ""
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace
    Write-Host ""
    [void](Read-Host "按 Enter 关闭")
    exit 1
}

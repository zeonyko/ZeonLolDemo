# Client/Assets/Game/Scripts/Shared -> repo Shared (junction)
# Encoding: UTF-8 BOM. Called by init-初始化.bat
$ErrorActionPreference = "Stop"

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Resolve-Path (Join-Path $ToolsDir "..")
$Shared = Join-Path $Root "Shared"
$ClientScripts = Join-Path $Root "Client\Assets\Game\Scripts"
$ClientLink = Join-Path $ClientScripts "Shared"
$ServerCsproj = Join-Path $Root "Server\Server.csproj"

function Get-LanHost {
    $ips = New-Object System.Collections.Generic.List[string]
    foreach ($nic in [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces()) {
        if ($nic.OperationalStatus -ne [System.Net.NetworkInformation.OperationalStatus]::Up) {
            continue
        }
        foreach ($addr in $nic.GetIPProperties().UnicastAddresses) {
            if ($addr.Address.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
                continue
            }
            $ip = $addr.Address.ToString()
            if ($ip.StartsWith("127.") -or $ip.StartsWith("169.254.")) {
                continue
            }
            if (-not $ips.Contains($ip)) {
                [void]$ips.Add($ip)
            }
        }
    }

    $sorted = $ips | Sort-Object {
        if ($_.StartsWith("192.168.")) { 0 }
        elseif ($_.StartsWith("10.")) { 1 }
        else { 2 }
    }
    $arr = @($sorted)
    if ($arr.Count -eq 0) { return "" }
    return [string]$arr[0]
}

function Get-UrlPort([string]$url, [int]$fallback) {
    if ([string]::IsNullOrWhiteSpace($url)) { return $fallback }
    $s = $url.Trim()
    $s = $s -replace '^https?://', ''
    $s = $s.Split('/')[0]
    if ($s -match ':(\d+)$') { return [int]$Matches[1] }
    return $fallback
}

function Write-BootConfig([string]$checkUrl) {
    $dir = Join-Path $Root "Client\Assets\StreamingAssets\Launch"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $file = Join-Path $dir "boot_config.json"
    $json = @"
{
  "version_check_url": "$checkUrl",
  "backup_version_check_url": ""
}
"@
    [System.IO.File]::WriteAllText($file, $json.Trim() + "`n", (New-Object System.Text.UTF8Encoding $false))
    Write-Host "[OK] boot_config.json = $checkUrl"
}

function Write-LocalDevJson {
    $file = Join-Path $ToolsDir "local_dev.json"
    $portVc = 8080
    $portGame = 8888
    if (Test-Path -LiteralPath $file) {
        try {
            $old = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($old.check_url) { $portVc = Get-UrlPort $old.check_url 8080 }
            elseif ($old.version_check_port) { $portVc = [int]$old.version_check_port }
            if ($old.server_url) { $portGame = Get-UrlPort $old.server_url 8888 }
            elseif ($old.game_server_port) { $portGame = [int]$old.game_server_port }
        }
        catch {
            Write-Host "[WARN] 旧 local_dev.json 读失败，用默认端口"
        }
    }

    $hostIp = Get-LanHost
    if ([string]::IsNullOrWhiteSpace($hostIp)) {
        Fail "没有探测到局域网 IP。连上网再跑一次 init。"
    }

    $checkUrl = "http://${hostIp}:${portVc}/version-check"
    $serverUrl = "${hostIp}:${portGame}"
    $cdnHost = "http://${hostIp}:${portVc}"

    $json = @"
{
  "_comment": "init 按本机 IP 生成。探测错了改这三项，再点 Launch/生成启动配置。再跑 init 会按本机 IP 重写。",
  "check_url": "$checkUrl",
  "server_url": "$serverUrl",
  "cdn_host": "$cdnHost"
}
"@
    [System.IO.File]::WriteAllText($file, $json.Trim() + "`n", (New-Object System.Text.UTF8Encoding $false))
    Write-Host "[OK] check_url  = $checkUrl"
    Write-Host "[OK] server_url = $serverUrl"
    Write-Host "[OK] cdn_host   = $cdnHost"
    Write-BootConfig $checkUrl
}

function Fail([string]$msg) {
    Write-Host "[ERROR] $msg" -ForegroundColor Red
    exit 1
}

Write-Host "========================================"
Write-Host "  Init"
Write-Host "========================================"
Write-Host "  Root: $Root"
Write-Host ""

Write-Host "[1/5] Check repo layout"
if (-not (Test-Path -LiteralPath (Join-Path $Shared "GameConstants.cs"))) {
    Fail "Missing Shared\GameConstants.cs : $Shared"
}
if (-not (Test-Path -LiteralPath $ServerCsproj)) {
    Fail "Missing Server\Server.csproj"
}
if (-not (Test-Path -LiteralPath $ClientScripts)) {
    Fail "Missing Client\Assets\Game\Scripts"
}
Write-Host "[OK] Shared / Server / Client"
Write-Host ""

Write-Host "[2/5] Link Client Shared"
Write-Host "      $ClientLink"
Write-Host "      -> $Shared"

New-Item -ItemType Directory -Force -Path $ClientScripts | Out-Null

if (Test-Path -LiteralPath $ClientLink) {
    $item = Get-Item -LiteralPath $ClientLink -Force
    $isReparse = [bool]($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
    if ($isReparse) {
        $cur = $item.Target
        if ($cur -is [array]) { $cur = $cur[0] }
        $curFull = [IO.Path]::GetFullPath([string]$cur).TrimEnd('\', '/')
        $wantFull = [IO.Path]::GetFullPath($Shared).TrimEnd('\', '/')
        if ($curFull -eq $wantFull) {
            Write-Host "[OK] junction already correct"
        } else {
            Write-Host "[INFO] rebuild junction"
            cmd /c "rmdir `"$ClientLink`""
            New-Item -ItemType Junction -Path $ClientLink -Target $Shared | Out-Null
            Write-Host "[OK] junction created"
        }
    } else {
        $files = @(Get-ChildItem -LiteralPath $ClientLink -Force -ErrorAction SilentlyContinue)
        if ($files.Count -eq 0) {
            Write-Host "[INFO] empty Shared folder, replace with junction"
            Remove-Item -LiteralPath $ClientLink -Force -Recurse
            New-Item -ItemType Junction -Path $ClientLink -Target $Shared | Out-Null
            Write-Host "[OK] junction created"
        } else {
            Fail "Client Shared is a real folder with files. Backup/delete it, then run init again.`n  $ClientLink"
        }
    }
} else {
    New-Item -ItemType Junction -Path $ClientLink -Target $Shared | Out-Null
    Write-Host "[OK] junction created"
}

if (-not (Test-Path -LiteralPath (Join-Path $ClientLink "GameConstants.cs"))) {
    Fail "Junction exists but GameConstants.cs is not readable"
}
Write-Host ""

Write-Host "[3/5] Check dotnet"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail "dotnet not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download"
}
dotnet --version
Write-Host "[OK] dotnet"
Write-Host ""

Write-Host "[4/5] Build Server"
dotnet build $ServerCsproj -c Debug -nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Fail "Server build failed"
}
Write-Host ""

Write-Host "[5/5] Write local_dev.json + boot_config.json"
Write-LocalDevJson
Write-Host ""

Write-Host "========================================"
Write-Host "[OK] Init done"
Write-Host "  Client Shared -> repo Shared\"
Write-Host "  local_dev.json + boot_config.json"
Write-Host "  Next: Tools\start_server-启动权威服务器.bat"
Write-Host "========================================"
exit 0

# start_server.ps1 — build & run authoritative server
# Encoding: UTF-8 BOM. Called by start_server-启动权威服务器.bat
$ErrorActionPreference = "Stop"

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ToolsDir "..")).Path
$ServerCsproj = Join-Path $Root "Server\Server.csproj"
$ClientLink = Join-Path $Root "Client\Assets\Game\Scripts\Shared\GameConstants.cs"
$DevEndpoints = Join-Path $ToolsDir "local_dev.json"
$Port = 8888

function Write-Info([string]$msg) { Write-Host "[INFO] $msg" }
function Write-Err([string]$msg) { Write-Host "[ERROR] $msg" -ForegroundColor Red }

function Read-DevEndpoints {
    if (-not (Test-Path -LiteralPath $DevEndpoints)) {
        return @{ server_port = 8888 }
    }
    try {
        $j = Get-Content -LiteralPath $DevEndpoints -Raw -Encoding UTF8 | ConvertFrom-Json
        $p = 8888
        if ($j.server_url) {
            $s = [string]$j.server_url
            if ($s -match ':(\d+)(/|$)') { $p = [int]$Matches[1] }
        }
        elseif ($j.game_server_port) { $p = [int]$j.game_server_port }
        return @{ server_port = $p }
    } catch {
        Write-Host "[WARN] bad Tools/local_dev.json: $($_.Exception.Message)" -ForegroundColor Yellow
        return @{ server_port = 8888 }
    }
}

function Stop-PortListeners([int]$port) {
    # Prefer netstat: Get-NetTCPConnection can hang on some Windows setups.
    $killed = $false
    $pattern = ":$port\s"
    netstat -ano | Select-String -Pattern $pattern | Select-String "LISTENING" | ForEach-Object {
        $procId = ($_.ToString().Trim() -split "\s+")[-1]
        if ($procId -match "^\d+$" -and [int]$procId -gt 0) {
            Stop-Process -Id ([int]$procId) -Force -ErrorAction SilentlyContinue
            Write-Host "[OK] killed PID $procId"
            $killed = $true
        }
    }

    if (-not $killed) {
        Write-Host "[OK] port $port is free"
    }
}

$dev = Read-DevEndpoints
$Port = [int]$dev.server_port
if ($Port -le 0) { $Port = 8888 }

Write-Host "========================================"
Write-Host "  MyGameDemo Server start / restart"
Write-Host "========================================"
Write-Host "  Root: $Root"
Write-Host "  Port: $Port  (Tools/local_dev.json)"
Write-Host ""

if (-not (Test-Path -LiteralPath $ServerCsproj)) {
    Write-Err "Missing Server\Server.csproj"
    Write-Host "       expected: $ServerCsproj"
    exit 1
}

if (-not (Test-Path -LiteralPath $ClientLink)) {
    Write-Host "[WARN] Client Shared link missing. Run Tools\init-初始化.bat." -ForegroundColor Yellow
    Write-Host "       Server can still start (csproj compiles repo Shared)."
    Write-Host ""
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Err "dotnet not found. Install .NET 8 SDK:"
    Write-Host "       https://dotnet.microsoft.com/download"
    exit 1
}

Write-Info "free port $Port if occupied"
Stop-PortListeners $Port
Start-Sleep -Seconds 1
Write-Host ""

Write-Info "build Server ..."
& dotnet build $ServerCsproj -c Debug -nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Err "build failed, not starting"
    exit $LASTEXITCODE
}
Write-Host ""

Write-Info "listen on 0.0.0.0:$Port (all NICs)"
Write-Info "game host: VersionCheck -> sandbox (not boot_config)"
Write-Info "configs: Server\Config"
Write-Info "phone: same Wi-Fi, firewall allow TCP $Port"
Write-Info "close this window or Ctrl+C to stop; run again to restart"
try {
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -ne "WellKnown" } |
        ForEach-Object { Write-Host ("[INFO] LAN connect -> {0}:{1}" -f $_.IPAddress, $Port) }
} catch { }
Write-Host "========================================"
Write-Host ""

$env:GAME_SERVER_PORT = "$Port"
& dotnet run --project $ServerCsproj -c Debug --no-build -- --port $Port
$exitCode = $LASTEXITCODE

Write-Host ""
Write-Host "========================================"
if ($exitCode -ne 0) {
    Write-Err "Server exit code $exitCode"
} else {
    Write-Info "Server stopped normally"
}
Write-Host "========================================"
exit $exitCode

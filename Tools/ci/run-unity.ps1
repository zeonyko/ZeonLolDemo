# Launch Unity batchmode for this repo.
# Usage: .\run-unity.ps1 -Platform Win64|Android -Task PublishPatch|BuildPlayer
# Encoding: UTF-8 BOM
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Win64", "Android")]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [ValidateSet("PublishPatch", "BuildPlayer")]
    [string]$Task
)

$ErrorActionPreference = "Stop"
$CiDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $CiDir "..\..")
$ProjectPath = Join-Path $RepoRoot "Client"
$VersionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
$LogDir = Join-Path $CiDir "logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if (-not (Test-Path $VersionFile)) {
    throw "Missing ProjectVersion.txt: $VersionFile"
}

$unityVersion = (Select-String -Path $VersionFile -Pattern "m_EditorVersion:\s*(.+)$").Matches[0].Groups[1].Value.Trim()
if ([string]::IsNullOrWhiteSpace($unityVersion)) {
    throw "Cannot parse Unity version."
}

function Find-UnityExe([string]$version) {
    if ($env:UNITY_EDITOR -and (Test-Path $env:UNITY_EDITOR)) {
        return $env:UNITY_EDITOR
    }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "C:\Program Files (x86)\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "D:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "D:\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "E:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "E:\Unity\Hub\Editor\$version\Editor\Unity.exe"
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }

    # Hub editors.json: look for any Unity.exe under ...\Hub\Editor\<version>\Editor\
    $hubEditors = Join-Path $env:APPDATA "UnityHub\editors.json"
    if (Test-Path $hubEditors) {
        $raw = Get-Content $hubEditors -Raw
        $needle = "Hub\Editor\$version\Editor\Unity.exe"
        $idx = $raw.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase)
        if ($idx -lt 0) {
            $needle = "Hub\\Editor\\$version\\Editor\\Unity.exe"
            $idx = $raw.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase)
        }
        if ($idx -ge 0) {
            $start = $idx
            while ($start -gt 0 -and $raw[$start - 1] -match '[A-Za-z0-9_.:\\/ ]') { $start-- }
            $path = $raw.Substring($start, ($idx + $needle.Length) - $start)
            $path = $path -replace '/', '\'
            $path = $path -replace '\\\\', '\'
            if ($path -match '^[A-Za-z]:\\' -and (Test-Path $path)) {
                return $path
            }
        }
    }

    return $null
}

function Write-LiveLogLine([string]$line) {
    if ([string]::IsNullOrWhiteSpace($line)) { return }
    if ($line -match "\[LaunchCI\]") {
        Write-Host $line -ForegroundColor Cyan
        return
    }
    if ($line -match "(?i)error CS|BuildFailed|Exception:|Prepare") {
        if ($line -match "(?i)error CS|BuildFailed|Exception:|失败|失败") {
            Write-Host $line -ForegroundColor Red
        }
    }
}

function Watch-UnityLog([string]$logFile, $proc) {
    $offset = [int64]0
    $lastHeartbeat = Get-Date
    while (-not $proc.HasExited) {
        if (Test-Path $logFile) {
            $fs = [System.IO.File]::Open($logFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try {
                $fs.Seek($offset, [System.IO.SeekOrigin]::Begin) | Out-Null
                $sr = New-Object System.IO.StreamReader($fs)
                while (($line = $sr.ReadLine()) -ne $null) {
                    Write-LiveLogLine $line
                }
                $offset = $fs.Position
            } finally {
                $fs.Dispose()
            }
        }

        if (((Get-Date) - $lastHeartbeat).TotalSeconds -ge 20) {
            Write-Host ("[{0}] Unity still running..." -f (Get-Date -Format "HH:mm:ss")) -ForegroundColor DarkGray
            $lastHeartbeat = Get-Date
        }
        Start-Sleep -Milliseconds 400
    }

    if (Test-Path $logFile) {
        $fs = [System.IO.File]::Open($logFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $fs.Seek($offset, [System.IO.SeekOrigin]::Begin) | Out-Null
            $sr = New-Object System.IO.StreamReader($fs)
            while (($line = $sr.ReadLine()) -ne $null) {
                Write-LiveLogLine $line
            }
        } finally {
            $fs.Dispose()
        }
    }
}

function Get-OutputPath([string]$logFile, [string]$lastOutputFile) {
    if (Test-Path $lastOutputFile) {
        $fromFile = (Get-Content $lastOutputFile -Raw).Trim()
        if ($fromFile) { return $fromFile }
    }
    if (-not (Test-Path $logFile)) { return "" }
    $match = Select-String -Path $logFile -Pattern "\[LaunchCI\] OUTPUT=(.+)$" | Select-Object -Last 1
    if ($match) { return $match.Matches[0].Groups[1].Value.Trim() }
    return ""
}

$unity = Find-UnityExe $unityVersion
if (-not $unity) {
    throw "Unity $unityVersion not found. Install via Hub, or set UNITY_EDITOR to Unity.exe."
}

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logFile = Join-Path $LogDir "$Task-$Platform-$stamp.log"
$lastOutputFile = Join-Path $LogDir "last-output.txt"
if (Test-Path $lastOutputFile) { Remove-Item $lastOutputFile -Force }
$buildTarget = $Platform
$method = "Launch.Editor.LaunchCi.$Task"

Write-Host "============================================================"
Write-Host " Task     $Task"
Write-Host " Platform $Platform"
Write-Host " Unity    $unityVersion"
Write-Host " Project  $ProjectPath"
Write-Host " Log      $logFile"
Write-Host "============================================================"
Write-Host "Close Unity Editor for this project first."
Write-Host ""

$unityArgs = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $ProjectPath,
    "-buildTarget", $buildTarget,
    "-executeMethod", $method,
    "-logFile", $logFile
)

$proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
Watch-UnityLog -logFile $logFile -proc $proc
$proc.WaitForExit() | Out-Null
$code = $proc.ExitCode
$output = Get-OutputPath $logFile $lastOutputFile

Write-Host ""
Write-Host "============================================================"
if ($code -eq 0) {
    Write-Host " Result   OK"
} else {
    Write-Host " Result   FAIL  exit=$code" -ForegroundColor Red
    if (Test-Path $logFile) {
        Write-Host " ---- log tail ----" -ForegroundColor Yellow
        Get-Content $logFile -Tail 40
    }
}

if ($Task -eq "BuildPlayer") {
    Write-Host " Output   $(if ($output) { $output } else { '(missing, see log)' })"
    if ($output) {
        $outDir = Split-Path -Parent $output
        if ($Platform -eq "Android") { $outDir = Split-Path -Parent $output }
        Write-Host " Folder   $outDir"
    }
} else {
    Write-Host " CDN      $(if ($output) { $output } else { '(missing, see log)' })"
}
Write-Host " Log      $logFile"
Write-Host "============================================================"

if ($code -ne 0) { exit $code }
exit 0
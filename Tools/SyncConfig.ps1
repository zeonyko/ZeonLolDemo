# Sync Config/ -> Client/Assets/Game/Configs + Server/Config
# Rules: Config/config.sync.json (copy | skip | logicOnly)
# Encoding: UTF-8. Called by sync_config-同步配置表.bat
$ErrorActionPreference = "Stop"

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Resolve-Path (Join-Path $ToolsDir "..")
$ConfigRoot = Join-Path $Root "Config"
$ManifestPath = Join-Path $ConfigRoot "config.sync.json"

function Fail([string]$msg) {
    Write-Host "[ERROR] $msg" -ForegroundColor Red
    exit 1
}

function Get-RelativeUnix([string]$root, [string]$absolutePath) {
    $full = [IO.Path]::GetFullPath($absolutePath)
    $rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        return [IO.Path]::GetFileName($full)
    }
    return $full.Substring($rootFull.Length).Replace([IO.Path]::DirectorySeparatorChar, '/')
}

function Resolve-TargetPath([string]$configRoot, [string]$relativeOrAbsolute) {
    if ([IO.Path]::IsPathRooted($relativeOrAbsolute)) {
        return [IO.Path]::GetFullPath($relativeOrAbsolute)
    }
    return [IO.Path]::GetFullPath((Join-Path $configRoot $relativeOrAbsolute))
}

function Clear-JsonTree([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return }
    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -ne ".meta" } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}

function Get-Matches([string]$configRoot, [string]$pattern) {
    $pattern = $pattern.Replace('\', '/').Trim('/')
    $results = New-Object System.Collections.Generic.List[string]

    if ($pattern.EndsWith("/**")) {
        $dirRel = $pattern.Substring(0, $pattern.Length - 3)
        $dirAbs = Join-Path $configRoot ($dirRel.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (Test-Path -LiteralPath $dirAbs) {
            Get-ChildItem -LiteralPath $dirAbs -Recurse -File |
                Where-Object { $_.Extension -ne ".meta" } |
                ForEach-Object { $results.Add((Get-RelativeUnix $configRoot $_.FullName)) }
        }
        return $results
    }

    $slash = $pattern.LastIndexOf('/')
    if ($slash -lt 0) {
        if ($pattern.Contains('*')) {
            Get-ChildItem -LiteralPath $configRoot -Recurse -File -Filter $pattern |
                Where-Object { $_.Extension -ne ".meta" } |
                ForEach-Object { $results.Add((Get-RelativeUnix $configRoot $_.FullName)) }
        }
        else {
            $abs = Join-Path $configRoot $pattern
            if (Test-Path -LiteralPath $abs) { $results.Add($pattern) }
        }
        return $results
    }

    $dir = $pattern.Substring(0, $slash)
    $filePattern = $pattern.Substring($slash + 1)
    $searchDir = Join-Path $configRoot ($dir.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (Test-Path -LiteralPath $searchDir) {
        Get-ChildItem -LiteralPath $searchDir -File -Filter $filePattern |
            Where-Object { $_.Extension -ne ".meta" } |
            ForEach-Object { $results.Add((Get-RelativeUnix $configRoot $_.FullName)) }
    }
    return $results
}

function ConvertTo-LogicOnly([string]$text, [string]$sourcePath) {
    if ($text -notmatch '"Logic"') { return $text }

    $obj = $text | ConvertFrom-Json
    if ($null -eq $obj.Logic) {
        Fail "logicOnly failed (no Logic): $sourcePath"
    }

    $wrapper = [ordered]@{ Logic = $obj.Logic }
    return ($wrapper | ConvertTo-Json -Depth 100)
}

function Transform-Content([string]$text, [string]$mode, [string]$sourcePath) {
    if ([string]::IsNullOrEmpty($mode) -or $mode -eq "copy") { return $text }
    if ($mode -eq "skip") { return $null }
    if ($mode -eq "logicOnly") { return (ConvertTo-LogicOnly $text $sourcePath) }
    Fail "Unknown sync mode '$mode' ($sourcePath)"
}

function Write-Target([string]$dstRoot, [string]$relativePath, [string]$content) {
    $dst = Join-Path $dstRoot ($relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
    $dir = Split-Path $dst -Parent
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    [IO.File]::WriteAllText($dst, $content, [Text.UTF8Encoding]::new($false))
}

# --- main ---
Write-Host "========================================"
Write-Host "  Sync Config"
Write-Host "========================================"
Write-Host "  Root: $Root"
Write-Host ""

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    Fail "Missing config.sync.json: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $manifest.entries -or $manifest.entries.Count -eq 0) {
    Fail "config.sync.json has no entries"
}
if ($null -eq $manifest.targets -or
    [string]::IsNullOrWhiteSpace($manifest.targets.client) -or
    [string]::IsNullOrWhiteSpace($manifest.targets.server)) {
    Fail "config.sync.json missing targets.client / targets.server"
}

$clientDst = Resolve-TargetPath $ConfigRoot $manifest.targets.client
$serverDst = Resolve-TargetPath $ConfigRoot $manifest.targets.server

Write-Host "  Source: $ConfigRoot"
Write-Host "  Client: $clientDst"
Write-Host "  Server: $serverDst"
Write-Host ""

Clear-JsonTree $clientDst
Clear-JsonTree $serverDst
New-Item -ItemType Directory -Force -Path $clientDst | Out-Null
New-Item -ItemType Directory -Force -Path $serverDst | Out-Null

$matched = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$clientFiles = 0
$serverFiles = 0
$logicOnlyFiles = 0

foreach ($entry in $manifest.entries) {
    if ($null -eq $entry -or [string]::IsNullOrWhiteSpace($entry.src)) { continue }

    $files = Get-Matches $ConfigRoot $entry.src.Trim()
    foreach ($rel in $files) {
        [void]$matched.Add($rel)
        $abs = Join-Path $ConfigRoot ($rel.Replace('/', [IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $abs)) { continue }

        $text = [IO.File]::ReadAllText($abs, [Text.Encoding]::UTF8)

        if ($entry.client -ne "skip") {
            $out = Transform-Content $text $entry.client $abs
            Write-Target $clientDst $rel $out
            $clientFiles++
        }

        if ($entry.server -ne "skip") {
            $out = Transform-Content $text $entry.server $abs
            Write-Target $serverDst $rel $out
            $serverFiles++
            if ($entry.server -eq "logicOnly") { $logicOnlyFiles++ }
        }
    }
}

$unmatched = @()
Get-ChildItem -LiteralPath $ConfigRoot -Recurse -File |
    Where-Object {
        $_.Name -ne "config.sync.json" -and
        $_.Name -ne "README.md" -and
        $_.Extension -ne ".meta"
    } |
    ForEach-Object {
        $rel = Get-RelativeUnix $ConfigRoot $_.FullName
        if (-not $matched.Contains($rel)) {
            $unmatched += $rel
            Write-Host "[WARN] unmatched (not synced): $rel" -ForegroundColor Yellow
        }
    }

Write-Host ""
Write-Host "========================================"
Write-Host "[OK] Client files: $clientFiles"
Write-Host "[OK] Server files: $serverFiles (logicOnly: $logicOnlyFiles)"
if ($unmatched.Count -gt 0) {
    Write-Host "[WARN] Unmatched: $($unmatched.Count)" -ForegroundColor Yellow
}
else {
    Write-Host "[OK] All authoritative files matched rules"
}
Write-Host "========================================"
exit 0

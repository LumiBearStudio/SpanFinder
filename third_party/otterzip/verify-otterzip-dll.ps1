<#
.SYNOPSIS
    Gates the vendored otterzip_ffi.dll before it can ship.

.DESCRIPTION
    Run this whenever the DLL is replaced, and before every release build.

    The CRT check is the one that matters most. Building otterzip_ffi.dll
    WITHOUT `-C target-feature=+crt-static` makes it import VCRUNTIME140.dll /
    VCRUNTIME140_1.dll / MSVCP140.dll. Those are present on any machine with
    Visual Studio installed — so the failure is invisible during development —
    but absent on a clean Windows install, where the DLL fails to load and the
    app dies at launch. That exact defect failed Microsoft Store certification
    for OtterZip on 2026-07-21 under policy 10.1.2.10 ("product crashes at
    launch"). This script exists so it cannot happen to SPAN.

    Checks performed:
      1. File exists and matches the sha256 recorded in VERSION.txt
      2. PE machine type is x64
      3. No CRT redistributable imports
      4. Exports the FFI entry points SPAN calls

.EXAMPLE
    pwsh third_party\otterzip\verify-otterzip-dll.ps1
#>
[CmdletBinding()]
param(
    [string]$DllPath,
    [string]$VersionFile
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not reliably populated inside param() defaults across
# PowerShell hosts, so resolve the script directory here instead.
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $DllPath)     { $DllPath     = Join-Path $scriptRoot 'x64\otterzip_ffi.dll' }
if (-not $VersionFile) { $VersionFile = Join-Path $scriptRoot 'VERSION.txt' }

$failures = @()

function Report([string]$name, [bool]$ok, [string]$detail) {
    $tag = if ($ok) { '[PASS]' } else { '[FAIL]' }
    Write-Host ("{0} {1}" -f $tag, $name)
    if ($detail) { Write-Host ("        {0}" -f $detail) }
    if (-not $ok) { $script:failures += $name }
}

Write-Host "otterzip_ffi.dll ship gate"
Write-Host "  target: $DllPath"
Write-Host ''

# --- 1. presence + integrity -------------------------------------------------
if (-not (Test-Path -LiteralPath $DllPath)) {
    Report 'file exists' $false "not found: $DllPath"
    Write-Host "`nFAILED" -ForegroundColor Red
    exit 1
}
Report 'file exists' $true ("{0:N0} bytes" -f (Get-Item -LiteralPath $DllPath).Length)

$actualSha = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (Test-Path -LiteralPath $VersionFile) {
    $recorded = (Select-String -LiteralPath $VersionFile -Pattern '^\s*sha256\s*:\s*([0-9a-fA-F]{64})' |
                 Select-Object -First 1).Matches.Groups[1].Value
    if ($recorded) {
        $match = ($recorded.ToLowerInvariant() -eq $actualSha)
        Report 'sha256 matches VERSION.txt' $match "actual=$actualSha"
    } else {
        Report 'sha256 recorded in VERSION.txt' $false 'no sha256 line found'
    }
} else {
    Report 'VERSION.txt present' $false "not found: $VersionFile"
}

# --- locate dumpbin ----------------------------------------------------------
$dumpbin = Get-Command dumpbin.exe -ErrorAction SilentlyContinue |
           Select-Object -ExpandProperty Source -First 1
if (-not $dumpbin) {
    $dumpbin = Get-ChildItem -Path @(
        "${env:ProgramFiles}\Microsoft Visual Studio",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
    ) -Filter dumpbin.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\Hostx64\\x64\\' } |
        Select-Object -ExpandProperty FullName -First 1
}
if (-not $dumpbin) {
    Report 'dumpbin available' $false 'install VS C++ build tools, or run from a Developer prompt'
    Write-Host "`nFAILED - cannot inspect the binary" -ForegroundColor Red
    exit 1
}

# --- 2. architecture ---------------------------------------------------------
$headers = & $dumpbin /headers $DllPath 2>&1 | Out-String
Report 'machine type is x64' ($headers -match '8664\s+machine\s+\(x64\)') 'PE header'

# --- 3. CRT redistributable imports (the Store-certification gate) -----------
$deps = & $dumpbin /dependents $DllPath 2>&1 | Out-String
$crt = [regex]::Matches($deps, '(?i)\b(VCRUNTIME140(_1)?|MSVCP140)\.dll\b') |
       ForEach-Object { $_.Value } | Sort-Object -Unique
Report 'no CRT redistributable imports' ($crt.Count -eq 0) $(
    if ($crt.Count) { "found: $($crt -join ', ') - rebuild with RUSTFLAGS=`"-C target-feature=+crt-static`"" }
    else { 'OS imports only' })

# --- 4. required exports -----------------------------------------------------
$exports = & $dumpbin /exports $DllPath 2>&1 | Out-String
$required = @(
    'otterzip_abi_version',
    'otterzip_archive_open',
    'otterzip_archive_close',
    'otterzip_archive_entry_count',
    'otterzip_archive_extract_all',
    'otterzip_last_error_message'
)
$missing = $required | Where-Object { $exports -notmatch [regex]::Escape($_) }
Report 'required FFI exports present' ($missing.Count -eq 0) $(
    if ($missing.Count) { "missing: $($missing -join ', ')" } else { "$($required.Count) checked" })

# --- verdict -----------------------------------------------------------------
Write-Host ''
if ($failures.Count) {
    Write-Host ("FAILED - {0} check(s): {1}" -f $failures.Count, ($failures -join '; ')) -ForegroundColor Red
    exit 1
}
Write-Host 'PASSED - safe to ship' -ForegroundColor Green
exit 0

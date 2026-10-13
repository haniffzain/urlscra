param(
    [switch]$RequireRust
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$manifest = Join-Path $root "native\urlscra-adblock\Cargo.toml"
$outDir = Join-Path $root "native\bin"
$outDll = Join-Path $outDir "urlscra_adblock.dll"

function Fail-Or-Warn {
    param([string]$Message)

    if ($RequireRust) {
        throw $Message
    }

    Write-Warning $Message
    exit 0
}

$cargo = Get-Command cargo -ErrorAction SilentlyContinue
if (-not $cargo) {
    Fail-Or-Warn "[URLScra] Rust cargo not found. Adblock Rust will be disabled until Rust is installed and this script is rerun."
}

Write-Host "[URLScra] Build adblock-rust native bridge"
& cargo build --release --manifest-path $manifest
if ($LASTEXITCODE -ne 0) {
    Fail-Or-Warn "[URLScra] adblock-rust native build failed with exit code $LASTEXITCODE."
}

$builtDll = Join-Path $root "native\urlscra-adblock\target\release\urlscra_adblock.dll"
if (-not (Test-Path $builtDll)) {
    Fail-Or-Warn "[URLScra] adblock-rust build completed but urlscra_adblock.dll was not found."
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item -Force $builtDll $outDll
Write-Host "[URLScra] Adblock Rust ready: $outDll" -ForegroundColor Green

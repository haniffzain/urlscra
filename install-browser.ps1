param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$browserPath = if (Test-Path "D:\") {
    "D:\URLScra\PlaywrightBrowsers"
} else {
    Join-Path $env:LOCALAPPDATA "URLScra\PlaywrightBrowsers"
}

New-Item -ItemType Directory -Force -Path $browserPath | Out-Null
$env:PLAYWRIGHT_BROWSERS_PATH = $browserPath
$env:URLSCRA_PLAYWRIGHT_BROWSERS_PATH = $browserPath
[Environment]::SetEnvironmentVariable("URLSCRA_PLAYWRIGHT_BROWSERS_PATH", $browserPath, "User")

Write-Host "[URLScra] Playwright browser path: $browserPath"

if (-not $SkipBuild) {
    Write-Host "[URLScra] Restore"
    dotnet restore URLScra.csproj

    Write-Host "[URLScra] Build Release"
    dotnet build URLScra.csproj -c Release --no-restore
}

$playwrightScript = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\playwright.ps1"
if (-not (Test-Path $playwrightScript)) {
    throw "Playwright install script not found: $playwrightScript"
}

Write-Host "[URLScra] Installing Chromium..."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $playwrightScript install chromium

Write-Host "[URLScra] Chromium installed successfully"
Write-Host "[URLScra] Stored at: $browserPath"

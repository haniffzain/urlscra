$ErrorActionPreference = "Stop"

$browserPath = if (Test-Path "D:\") {
    "D:\URLScra\PlaywrightBrowsers"
} else {
    Join-Path $env:LOCALAPPDATA "URLScra\PlaywrightBrowsers"
}

New-Item -ItemType Directory -Force -Path $browserPath | Out-Null
$env:PLAYWRIGHT_BROWSERS_PATH = $browserPath
[Environment]::SetEnvironmentVariable("URLSCRA_PLAYWRIGHT_BROWSERS_PATH", $browserPath, "User")

Write-Host "URLScra Playwright browser path: $browserPath"
Write-Host "Building URLScra..."
dotnet build -c Release

$playwrightScript = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\playwright.ps1"
if (-not (Test-Path $playwrightScript)) {
    throw "Playwright install script not found: $playwrightScript"
}

Write-Host "Installing Chromium..."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $playwrightScript install chromium

Write-Host "Chromium installed successfully."
Write-Host "Stored at: $browserPath"

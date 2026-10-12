param(
    [switch]$Publish,
    [switch]$NoRun,
    [switch]$SetupBrowser
)

$ErrorActionPreference = "Stop"

Write-Host "[URLScra] Restore"
dotnet restore URLScra.csproj

Write-Host "[URLScra] Build Release"
dotnet build URLScra.csproj -c Release --no-restore

if ($SetupBrowser) {
    Write-Host "[URLScra] Setup Playwright Chromium"
    & (Join-Path $PSScriptRoot "install-browser.ps1") -SkipBuild
}

if ($Publish) {
    Write-Host "[URLScra] Publish win-x64"
    dotnet publish URLScra.csproj `
        -c Release `
        -r win-x64 `
        --self-contained false `
        --no-build `
        -o publish\win-x64

    Write-Host "[URLScra] Published: publish\win-x64"
}
elif (-not $NoRun) {
    Write-Host "[URLScra] Run"
    dotnet run --project URLScra.csproj -c Release --no-build
}

Write-Host "[URLScra] Pipeline complete"

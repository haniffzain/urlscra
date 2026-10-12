param(
    [switch]$Publish,
    [switch]$NoRun,
    [switch]$SetupBrowser
)

$ErrorActionPreference = "Stop"

function Assert-LastExitCode {
    param([string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "[URLScra] $Step failed with exit code $LASTEXITCODE."
    }
}

Write-Host "[URLScra] Prepare app icon"
& (Join-Path $PSScriptRoot "ensure-icon.ps1")

Write-Host "[URLScra] Restore"
dotnet restore URLScra.csproj
Assert-LastExitCode "Restore"

Write-Host "[URLScra] Build Release"
dotnet build URLScra.csproj -c Release --no-restore
Assert-LastExitCode "Build Release"

if ($SetupBrowser) {
    Write-Host "[URLScra] Setup Playwright Chromium"
    & (Join-Path $PSScriptRoot "install-browser.ps1") -SkipBuild
    if ($LASTEXITCODE -ne 0) {
        throw "[URLScra] Browser setup failed with exit code $LASTEXITCODE."
    }
}

if ($Publish) {
    Write-Host "[URLScra] Publish win-x64"
    dotnet publish URLScra.csproj `
        -c Release `
        -r win-x64 `
        --self-contained false `
        --no-build `
        -o publish\win-x64
    Assert-LastExitCode "Publish win-x64"

    Write-Host "[URLScra] Published: publish\win-x64"
}
elif (-not $NoRun) {
    Write-Host "[URLScra] Run"
    dotnet run --project URLScra.csproj -c Release --no-build
    Assert-LastExitCode "Run"
}

Write-Host "[URLScra] Pipeline complete" -ForegroundColor Green

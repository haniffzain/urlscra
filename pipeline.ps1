param(
    [switch]$Publish,
    [switch]$NoRun,
    [switch]$SetupBrowser,
    [switch]$RequireAdblock
)

$ErrorActionPreference = "Stop"

function Assert-ExitCode {
    param(
        [int]$Code,
        [string]$Step
    )

    if ($Code -ne 0) {
        throw "[URLScra] $Step failed with exit code $Code."
    }
}

Write-Host "[URLScra] Prepare app icon"
& (Join-Path $PSScriptRoot "ensure-icon.ps1")

Write-Host "[URLScra] Prepare adblock-rust"
if ($RequireAdblock) {
    & (Join-Path $PSScriptRoot "build-adblock.ps1") -RequireRust
}
else {
    & (Join-Path $PSScriptRoot "build-adblock.ps1")
}

Write-Host "[URLScra] Restore"
dotnet restore URLScra.csproj
Assert-ExitCode $LASTEXITCODE "Restore"

Write-Host "[URLScra] Build Release"
dotnet build URLScra.csproj -c Release --no-restore
Assert-ExitCode $LASTEXITCODE "Build Release"

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
    Assert-ExitCode $LASTEXITCODE "Publish win-x64"

    Write-Host "[URLScra] Published: publish\win-x64"
}
elseif (-not $NoRun) {
    Write-Host "[URLScra] Run"
    dotnet run --project URLScra.csproj -c Release --no-build
    Assert-ExitCode $LASTEXITCODE "Run"
}

Write-Host "[URLScra] Pipeline complete" -ForegroundColor Green

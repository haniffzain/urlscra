param(
    [switch]$NoRun
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$branch = "m1-windows-scraper"
$base = "https://raw.githubusercontent.com/haniffzain/urlscra/$branch/"

Write-Host "[URLScra] Verified development sync" -ForegroundColor Cyan
Write-Host "[URLScra] Source branch: $branch"

Get-Process URLScra -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "[URLScra] Closing running URLScra process..." -ForegroundColor Yellow
    Stop-Process -Id $_.Id -Force
}

$files = @(
    "App.xaml",
    "App.xaml.cs",
    "MainWindow.xaml",
    "MainWindow.xaml.cs",
    "URLScra.csproj",
    "Models/RenderedPage.cs",
    "Models/ScrapeItem.cs",
    "Models/SmartRecord.cs",
    "Services/AdblockRustService.cs",
    "Services/BrowserRenderService.cs",
    "Services/CsvExporter.cs",
    "Services/HtmlDataAnalyzer.cs",
    "Services/ScraperService.cs",
    "Services/SmartHtmlAnalyzer.cs",
    "Services/SmartScraperService.cs",
    "Assets/adblock/urlscra-base.txt",
    "native/urlscra-adblock/Cargo.toml",
    "native/urlscra-adblock/src/lib.rs",
    "build-adblock.ps1",
    "pipeline.ps1",
    "install-browser.ps1",
    "ensure-icon.ps1",
    "THIRD_PARTY_NOTICES.md"
)

foreach ($file in $files) {
    $destination = Join-Path $root $file
    $directory = Split-Path $destination -Parent
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $temp = "$destination.download"
    Write-Host "[URLScra] Sync $file"
    Invoke-WebRequest -UseBasicParsing ($base + $file) -OutFile $temp
    Move-Item -Force $temp $destination
}

$projectText = Get-Content (Join-Path $root "URLScra.csproj") -Raw
$windowText = Get-Content (Join-Path $root "MainWindow.xaml") -Raw
$browserText = Get-Content (Join-Path $root "Services\BrowserRenderService.cs") -Raw
$adblockText = Get-Content (Join-Path $root "Services\AdblockRustService.cs") -Raw
$cargoText = Get-Content (Join-Path $root "native\urlscra-adblock\Cargo.toml") -Raw

if (-not $projectText.Contains('<Version>0.7.2</Version>')) {
    throw "Sync verification failed: expected URLScra version 0.7.2."
}

if (-not $windowText.Contains('Extract. Preview. Save.')) {
    throw "Sync verification failed: M2.3 UI was not downloaded."
}

if ($browserText.Contains('NetworkIdle')) {
    throw "Sync verification failed: stale BrowserRenderService still contains NetworkIdle."
}

if (-not $browserText.Contains('Adblock Rust active')) {
    throw "Sync verification failed: BrowserRenderService adblock integration missing."
}

if (-not $adblockText.Contains('urlscra_adblock_should_block')) {
    throw "Sync verification failed: C# adblock bridge missing."
}

if (-not $cargoText.Contains('adblock = "=0.13.3"')) {
    throw "Sync verification failed: adblock-rust 0.13.3 pin missing."
}

Write-Host "[URLScra] SYNC VERIFIED: v0.7.2 / M2.3.3 Adblock Rust" -ForegroundColor Green

Write-Host "[URLScra] Clean old build output"
dotnet clean (Join-Path $root "URLScra.csproj") -c Release | Out-Host
Remove-Item -Recurse -Force (Join-Path $root "bin"), (Join-Path $root "obj") -ErrorAction SilentlyContinue

if ($NoRun) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "pipeline.ps1") -NoRun
}
else {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "pipeline.ps1")
}

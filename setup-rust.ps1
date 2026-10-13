$ErrorActionPreference = "Stop"

$rustRoot = if (Test-Path "D:\") { "D:\URLScra\Rust" } else { Join-Path $env:LOCALAPPDATA "URLScra\Rust" }
$cargoHome = Join-Path $rustRoot "cargo"
$rustupHome = Join-Path $rustRoot "rustup"
$cargoBin = Join-Path $cargoHome "bin"
$installer = Join-Path $rustRoot "rustup-init.exe"

New-Item -ItemType Directory -Force -Path $rustRoot | Out-Null

Write-Host "[URLScra] Rust root: $rustRoot"
Write-Host "[URLScra] Downloading official rustup installer..."
Invoke-WebRequest "https://win.rustup.rs/x86_64" -OutFile $installer

$env:CARGO_HOME = $cargoHome
$env:RUSTUP_HOME = $rustupHome
[Environment]::SetEnvironmentVariable("CARGO_HOME", $cargoHome, "User")
[Environment]::SetEnvironmentVariable("RUSTUP_HOME", $rustupHome, "User")

Write-Host "[URLScra] Installing minimal stable Rust toolchain..."
& $installer -y --profile minimal --default-toolchain stable --no-modify-path
if ($LASTEXITCODE -ne 0) {
    throw "[URLScra] rustup installation failed with exit code $LASTEXITCODE."
}

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$parts = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($parts -notcontains $cargoBin) {
    $newPath = (($parts + $cargoBin) -join ';')
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
}

if (($env:Path -split ';') -notcontains $cargoBin) {
    $env:Path = "$cargoBin;$env:Path"
}

Write-Host "[URLScra] Rust installed." -ForegroundColor Green
& (Join-Path $cargoBin "rustc.exe") --version
& (Join-Path $cargoBin "cargo.exe") --version
Write-Host "[URLScra] Re-open PowerShell later to inherit the permanent PATH automatically."

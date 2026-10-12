$ErrorActionPreference = "Stop"
Write-Host "URLScra - restoring packages..."
dotnet restore
Write-Host "URLScra - starting..."
dotnet run

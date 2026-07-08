# build.ps1
$sdks = dotnet --list-sdks
$hasNet10 = $sdks -match "10.0"

if (-not $hasNet10) {
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Yellow
    Write-Host " WARNING: .NET 10.0 SDK is required but was not found!" -ForegroundColor Yellow
    Write-Host "==========================================================" -ForegroundColor Yellow
    Write-Host "To compile DWN.BRIDGE, you need the .NET 10 SDK."
    Write-Host ""
    
    $choice = Read-Host "Would you like to install .NET 10 SDK automatically via winget? (Y/N)"
    if ($choice -eq "Y" -or $choice -eq "y") {
        Write-Host "Launching installation... Please approve the Windows prompt if requested." -ForegroundColor Cyan
        winget install -e --id Microsoft.DotNet.SDK.10
        Write-Host "Installation completed! Please restart your terminal and run build.ps1 again." -ForegroundColor Green
    } else {
        Write-Host "Please install it manually from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Cyan
    }
    exit 1
}

# Se l'SDK è installato, esegui il build normalmente
Write-Host "Building project in Debug mode..." -ForegroundColor Green
dotnet build -c Debug

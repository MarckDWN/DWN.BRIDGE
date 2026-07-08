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
    
    $choice = Read-Host "Would you like to install .NET 10 SDK automatically? (Y/N)"
    if ($choice -eq "Y" -or $choice -eq "y") {
        Write-Host "Launching installation..." -ForegroundColor Cyan
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            Write-Host "Using winget to install .NET 10..." -ForegroundColor Cyan
            winget install -e --id Microsoft.DotNet.SDK.10
            Write-Host "Installation completed! Please restart your terminal and run build.ps1 again." -ForegroundColor Green
        } else {
            Write-Host "winget not found. Using Microsoft's official dotnet-install script..." -ForegroundColor Cyan
            $scriptPath = "$env:TEMP\dotnet-install.ps1"
            
            # Scarica lo script ufficiale dotnet-install.ps1
            Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $scriptPath
            
            # Esegui lo script indicando il canale 10.0
            & $scriptPath -Channel 10.0
            
            # Aggiungi temporaneamente la cartella locale al PATH della sessione corrente
            $localDotnetPath = "$env:LocalAppData\Microsoft\dotnet"
            if (Test-Path $localDotnetPath) {
                $env:PATH += ";$localDotnetPath"
                Write-Host "Local .NET SDK added to current session PATH." -ForegroundColor Green
                Write-Host "Retrying build now..." -ForegroundColor Green
                dotnet build -c Debug
                exit 0
            } else {
                Write-Host "Installation completed, but local path not found. Please download and install .NET 10 manually from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "Please install it manually from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Cyan
    }
    exit 1
}

# Se l'SDK è installato, esegui il build normalmente
Write-Host "Building project in Debug mode..." -ForegroundColor Green
dotnet build -c Debug

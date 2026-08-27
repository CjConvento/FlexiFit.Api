# redeploy-flexifit.ps1
# Run this as Administrator whenever you make changes to the FlexiFit source code in VS Code
# and want to update the version running on your local IIS.

# --- Check for Administrator privileges ---
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as administrator', then try again." -ForegroundColor Red
    exit 1
}

# --- Load IIS management module ---
Import-Module WebAdministration

# --- Variables Configuration for FlexiFit ---
$appPoolName = "FlexiFitApiPool"
$projectPath = "C:\FlexiFit.Api\FlexiFit.Api\FlexiFit.Api.csproj"
$outputPath  = "C:\inetpub\wwwroot\FlexiFit.Api"

Write-Host "Stopping App Pool: $appPoolName ..." -ForegroundColor Yellow
Stop-WebAppPool -Name $appPoolName

Write-Host "Waiting for worker process to release files..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

Write-Host "Publishing FlexiFit project..." -ForegroundColor Yellow
dotnet publish $projectPath -c Release -o $outputPath

Write-Host "Starting App Pool: $appPoolName ..." -ForegroundColor Yellow
Start-WebAppPool -Name $appPoolName

Write-Host "Done! Visit http://localhost:8090 to check your FlexiFit API." -ForegroundColor Green

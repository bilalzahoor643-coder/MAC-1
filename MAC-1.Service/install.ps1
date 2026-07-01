# MAC-1 Service Installer
# Run as Administrator

param(
    [switch]$Uninstall
)

$ServiceName = "MAC-1 Service"
$ServiceExe = Join-Path $PSScriptRoot "MAC-1.Service.exe"
$InstallDir = "$env:ProgramFiles\MAC-1"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: Run this script as Administrator!" -ForegroundColor Red
    exit 1
}

if ($Uninstall) {
    Write-Host "Uninstalling MAC-1 Service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Service uninstalled." -ForegroundColor Green
    exit 0
}

Write-Host "Installing MAC-1 Background Service..." -ForegroundColor Cyan

if (-not (Test-Path $ServiceExe)) {
    Write-Host "ERROR: MAC-1.Service.exe not found at: $ServiceExe" -ForegroundColor Red
    Write-Host "Build the service project first: dotnet publish -c Release" -ForegroundColor Yellow
    exit 1
}

New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null
Copy-Item -Path $ServiceExe -Destination $InstallDir -Force
Copy-Item -Path (Join-Path $PSScriptRoot "*.dll") -Destination $InstallDir -Force -ErrorAction SilentlyContinue

$ExePath = Join-Path $InstallDir "MAC-1.Service.exe"

sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto | Out-Null
sc.exe description $ServiceName "MAC-1 Download Manager Background Service - Receives download events from Chrome extension" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000 | Out-Null

Start-Service -Name $ServiceName

Write-Host ""
Write-Host "MAC-1 Service installed successfully!" -ForegroundColor Green
Write-Host "  Service Name: $ServiceName" -ForegroundColor White
Write-Host "  Install Path: $InstallDir" -ForegroundColor White
Write-Host "  Startup:      Automatic" -ForegroundColor White
Write-Host "  HTTP Server:  http://127.0.0.1:57575/" -ForegroundColor White
Write-Host ""
Write-Host "To uninstall: .\install.ps1 -Uninstall" -ForegroundColor Yellow

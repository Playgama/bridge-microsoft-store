<#
  Installs the most recently built MSIX on THIS PC for testing.
  Trusts the local test certificate (requires administrator) and installs the package.
  Run this via "Install for testing.bat".
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $root 'dist'

Write-Host "Playgama Bridge - install for testing" -ForegroundColor White

$cer = Join-Path $distDir 'PlaygamaTest.cer'
if (-not (Test-Path $cer)) {
    $cer = (Get-ChildItem (Join-Path $root 'signing\*.cer') -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}
if (-not $cer -or -not (Test-Path $cer)) { throw "Certificate not found. Run 'Build MSIX.bat' first." }

$msix = Get-ChildItem (Join-Path $distDir '*.msix') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $msix) { throw "No .msix found in 'dist'. Run 'Build MSIX.bat' first." }

Write-Host "Trusting certificate..." -ForegroundColor Cyan
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

Write-Host "Installing $($msix.Name)..." -ForegroundColor Cyan
Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion

Write-Host "`nInstalled. Launch 'PlaygamaBridgeMicrosoftStore' from the Start menu." -ForegroundColor Green

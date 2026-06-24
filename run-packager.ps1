<#  Launches the Packager GUI (builds it on first run). Installs .NET 8 SDK if missing. #>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $c = Get-Command dotnet -ErrorAction SilentlyContinue; if ($c) { $dotnet = $c.Source } }
$haveSdk = $false
if (Test-Path $dotnet) { try { if (& $dotnet --list-sdks 2>$null | Where-Object { $_ -match '^8\.' }) { $haveSdk = $true } } catch { } }
if (-not $haveSdk) {
    Write-Host "Installing .NET 8 SDK (one-time)..." -ForegroundColor Yellow
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements --disable-interactivity
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { throw "Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and try again." }
}

& $dotnet run --project (Join-Path $root 'tools\Packager\Packager.csproj') -c Release

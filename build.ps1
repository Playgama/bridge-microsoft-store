<#
  Playgama Bridge — one-step MSIX builder.

  For non-developers: just run "Build MSIX.bat" (double-click). This script will:
    1. Make sure the .NET 8 SDK is installed (installs it via winget if missing).
    2. Build the app (self-contained, no .NET needed on the player's PC).
    3. Create/reuse a local test signing certificate.
    4. Package and sign an installable .msix.

  Output goes to the "dist" folder next to this script.

  Optional:  powershell -File build.ps1 -Arch arm64        (build for ARM64 PCs)
#>

param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',
    # Optional: use a specific certificate file / password instead of the one in signing\.
    [string]$PfxPath,
    [string]$PfxPassword,
    # Optional: a publisher name from publishers.json (sets identity + cert + appsettings).
    [string]$Publisher
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }

$rid       = "win-$Arch"
$project     = Join-Path $root 'PlaygamaBridgeMicrosoftStore.csproj'
$manifest    = Join-Path $root 'Package.appxmanifest'
$pubDir      = Join-Path $root "build\publish-$Arch"
$distDir     = Join-Path $root 'dist'
$certPw      = '11111111'

Write-Host "Playgama Bridge - MSIX builder ($Arch)" -ForegroundColor White

# ---------------------------------------------------------------- 1. .NET SDK
Step "Checking .NET 8 SDK"
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnet = $cmd.Source }
}
$haveSdk = $false
if (Test-Path $dotnet) {
    try { if (& $dotnet --list-sdks 2>$null | Where-Object { $_ -match '^8\.' }) { $haveSdk = $true } } catch { }
}
if (-not $haveSdk) {
    Info ".NET 8 SDK not found - installing via winget (this can take a few minutes)..."
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements --disable-interactivity
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) {
        throw "Could not install the .NET 8 SDK automatically. Please install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again."
    }
}
Ok "Using $dotnet"

# ---------------------------------------------------------------- 1b. Publisher profile
if ($Publisher) {
    Step "Applying publisher profile: $Publisher"
    . (Join-Path $root 'tools\publishers.ps1')
    $pub = Get-PublisherProfile $root $Publisher
    $info = Set-PublisherInfo $pub $root $manifest (Join-Path $root 'appsettings.json')
    if (-not $PfxPath -and $info.PfxPath) { $PfxPath = $info.PfxPath }
    if (-not $PfxPassword -and $info.PfxPassword) { $certPw = $info.PfxPassword; $PfxPassword = $info.PfxPassword }
    Ok "Applied publisher identity from publishers.json"
}

# ---------------------------------------------------------------- 2. Build / publish
Step "Building the app (self-contained $Arch)"
if (Test-Path $pubDir) { Remove-Item -Recurse -Force -LiteralPath $pubDir }
& $dotnet publish $project -c Release -r $rid --self-contained true -p:Platform=$Arch -o $pubDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed. See the messages above." }
if (-not (Test-Path (Join-Path $pubDir 'PlaygamaBridgeMicrosoftStore.exe'))) { throw "Build did not produce the app executable." }
Ok "Built to $pubDir"

# ---------------------------------------------------------------- 3. Locate packaging tools
Step "Locating packaging tools (makeappx / signtool)"
function Find-Tool($name) {
    $fromNuget = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools*\*\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName | Select-Object -Last 1
    if ($fromNuget) { return $fromNuget.FullName }
    $fromKit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue |
        Sort-Object FullName | Select-Object -Last 1
    if ($fromKit) { return $fromKit.FullName }
    return $null
}
$makeappx = Find-Tool 'makeappx.exe'
$signtool = Find-Tool 'signtool.exe'
$makepri  = Find-Tool 'makepri.exe'
if (-not $makeappx -or -not $signtool -or -not $makepri) {
    throw "Could not find makeappx/signtool/makepri. Run the build once more (NuGet restore provides them), or install the Windows 10/11 SDK."
}
Ok "makeappx: $makeappx"

# ---------------------------------------------------------------- 4. Signing certificate
Step "Preparing signing certificate"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$signingDir = Join-Path $root 'signing'
if ($PfxPassword) { $certPw = $PfxPassword }

# Pick the certificate to sign with, in priority order:
#   1) -PfxPath you pass in
#   2) any *.pfx in the signing\ folder (shared team cert; name does not matter)
#   3) create a new self-signed one in signing\ (first-time fallback)
$pfx = $null
if ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { throw "PfxPath not found: $PfxPath" }
    $pfx = (Resolve-Path $PfxPath).Path
    Info "Using certificate from -PfxPath"
}
elseif (Test-Path $signingDir) {
    $found = Get-ChildItem (Join-Path $signingDir '*.pfx') -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $pfx = $found.FullName; Info "Using shared team certificate: signing\$($found.Name)" }
}
if (-not $pfx) {
    Info "No certificate found - creating signing\PlaygamaTest.pfx (CN=Asus) ..."
    New-Item -ItemType Directory -Force -Path $signingDir | Out-Null
    $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=Asus' -KeyUsage DigitalSignature `
        -FriendlyName 'Playgama Bridge Test' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    $securePw = ConvertTo-SecureString -String $certPw -Force -AsPlainText
    $pfx = Join-Path $signingDir 'PlaygamaTest.pfx'
    Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfx -Password $securePw | Out-Null
    Export-Certificate   -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath (Join-Path $signingDir 'PlaygamaTest.cer') | Out-Null
    Ok "Created certificate - share the .pfx with teammates (out of band; do not commit it)"
}

# The package Publisher MUST exactly equal the certificate's subject, or Windows refuses to
# install it. Read the cert subject and verify (and tell you what to set if it differs).
$x509 = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $certPw)
$certSubject = $x509.Subject
[xml]$manifestXml = Get-Content $manifest
$publisher = $manifestXml.Package.Identity.Publisher
function Normalize-Dn($s) { ($s -replace '\s', '').ToUpperInvariant() }
if ((Normalize-Dn $certSubject) -ne (Normalize-Dn $publisher)) {
    throw @"
Certificate / manifest mismatch.
  Certificate subject : $certSubject
  Manifest Publisher  : $publisher
Open Package.appxmanifest and set Publisher to EXACTLY:
  Publisher="$certSubject"
then run this again.
"@
}
Ok "Certificate OK (Publisher=$certSubject)"

# Export the public .cer next to the package so 'Install for testing' can trust it.
$cer = Join-Path $distDir 'PlaygamaTest.cer'
[System.IO.File]::WriteAllBytes($cer, $x509.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

# ---------------------------------------------------------------- 5. Bump version + stage manifest
Step "Preparing package manifest"
[xml]$xml = Get-Content $manifest
$ver = [Version]$xml.Package.Identity.Version
$newVer = "{0}.{1}.{2}.{3}" -f $ver.Major, $ver.Minor, $ver.Build, ($ver.Revision + 1)
$xml.Package.Identity.Version = $newVer
$xml.Save($manifest)                                   # persist the bump
Copy-Item $manifest (Join-Path $pubDir 'AppxManifest.xml') -Force
Ok "Version $newVer"

# ---------------------------------------------------------------- 5b. Resource index (icons)
# Without resources.pri Windows can't resolve scale-200 / targetsize icon variants and falls
# back to the small 44px base logo (icons look tiny). makepri builds that index.
Step "Indexing resources (resources.pri)"
# Default qualifier includes scale-200 so a scale-200-only asset set is valid as the default
# (no scale-100 base needed) — this is what makes the icon render full-size, like VS does.
$priConfig = Join-Path $env:TEMP 'playgama-priconfig.xml'
& $makepri createconfig /cf $priConfig /dq lang-en-US_scale-200 /o | Out-Null
$priOut = Join-Path $pubDir 'resources.pri'
& $makepri new /pr $pubDir /cf $priConfig /mn (Join-Path $pubDir 'AppxManifest.xml') /of $priOut /o | Out-Null
if (-not (Test-Path $priOut)) { throw "Failed to generate resources.pri (makepri)." }
Ok "resources.pri created"

# ---------------------------------------------------------------- 6. Pack + sign
Step "Packaging and signing"
$msix = Join-Path $distDir ("PlaygamaBridgeMicrosoftStore_{0}_{1}.msix" -f $newVer, $Arch)
if (Test-Path $msix) { Remove-Item -Force -LiteralPath $msix }
& $makeappx pack /d $pubDir /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "Packaging (makeappx) failed." }
& $signtool sign /fd SHA256 /f $pfx /p $certPw $msix
if ($LASTEXITCODE -ne 0) { throw "Signing (signtool) failed." }

Step "DONE"
Ok "Package : $msix"
Ok "Cert    : $cer"
Write-Host ""
Write-Host "To test on THIS PC: run 'Install for testing.bat'." -ForegroundColor Yellow
Write-Host "To publish to the Microsoft Store: upload the .msix in Partner Center (the Store re-signs it)." -ForegroundColor Yellow

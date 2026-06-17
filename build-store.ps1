<#
  Builds a RELEASE package (.msixbundle) for the Microsoft Store.

  Differences from the local 'build.ps1':
    * Builds BOTH x64 and arm64 and combines them into one .msixbundle.
    * Does NOT sign it - the Microsoft Store re-signs your package on upload.
    * Uses a Store-legal version (the 4th number / "revision" is forced to 0).

  IMPORTANT - before your first Store build, set your app identity in
  Package.appxmanifest to the values from Partner Center:
    <Identity Name="..." Publisher="CN=..." Version="x.y.z.0" />
  (Partner Center > your app > Product management > Product identity.)

  Usage:
    powershell -File build-store.ps1                 # version from manifest, revision -> 0
    powershell -File build-store.ps1 -Version 1.2.0.0
    powershell -File build-store.ps1 -Arch x64       # single architecture
#>

param(
    [string]$Version,
    [ValidateSet('x64', 'arm64', 'both')]
    [string]$Arch = 'both',
    # Optional: a publisher name from publishers.json (sets identity + appsettings).
    [string]$Publisher
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Step($m) { Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Info($m) { Write-Host "    $m" -ForegroundColor Gray }
function Ok($m)   { Write-Host "    $m" -ForegroundColor Green }
function Warn($m) { Write-Host "    $m" -ForegroundColor Yellow }

$project     = Join-Path $root 'PlaygamaBridgeMicrosoftStore.csproj'
$manifest    = Join-Path $root 'Package.appxmanifest'
$appSettings = Join-Path $root 'appsettings.json'
$outDir      = Join-Path $root 'dist-store'
$stageDir    = Join-Path $root 'build\store-packages'

$arches = if ($Arch -eq 'both') { @('x64', 'arm64') } else { @($Arch) }

Write-Host "Playgama Bridge - Microsoft Store build" -ForegroundColor White

# ---- .NET SDK -------------------------------------------------------------
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $c = Get-Command dotnet -ErrorAction SilentlyContinue; if ($c) { $dotnet = $c.Source } }
if (-not (Test-Path $dotnet)) { throw "Run 'Build MSIX.bat' once first (it installs the .NET 8 SDK)." }

# ---- Tools ----------------------------------------------------------------
function Find-Tool($name) {
    $n = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools*\*\bin\*\x64\$name" -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
    if ($n) { return $n.FullName }
    $k = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
    if ($k) { return $k.FullName }
    throw "$name not found. Run 'Build MSIX.bat' once so NuGet restores the packaging tools."
}
$makeappx = Find-Tool 'makeappx.exe'

# ---- Publisher profile ----------------------------------------------------
if ($Publisher) {
    Step "Applying publisher profile: $Publisher"
    . (Join-Path $root 'tools\publishers.ps1')
    $pub = Get-PublisherProfile $root $Publisher
    [void](Set-PublisherInfo $pub $root $manifest $appSettings)
    Ok "Applied identity + appsettings from publishers.json"
}

# ---- Identity / version checks --------------------------------------------
Step "Checking app identity"
[xml]$m = Get-Content $manifest
$idName = $m.Package.Identity.Name
$idPub  = $m.Package.Identity.Publisher
if ($idName -eq '71b6ebd7-d6b7-4d43-98cd-906ec33f1c17' -or $idPub -eq 'CN=Asus') {
    Warn "Identity looks like the local placeholder:"
    Warn "  Name=$idName  Publisher=$idPub"
    Warn "The Store will REJECT this. Set Package.appxmanifest <Identity> to your"
    Warn "Partner Center values before submitting. (Building anyway for inspection.)"
}
else { Ok "Identity: Name=$idName Publisher=$idPub" }

if (-not $Version) {
    $v = [Version]$m.Package.Identity.Version
    $Version = "{0}.{1}.{2}.0" -f $v.Major, $v.Minor, $v.Build      # Store requires revision = 0
}
$vp = [Version]$Version
if ($vp.Revision -ne 0) { throw "Store version must end in .0 (revision reserved by Store). Got $Version." }
Ok "Package version: $Version"

# ---- Build each architecture ----------------------------------------------
if (Test-Path $stageDir) { Remove-Item -Recurse -Force -LiteralPath $stageDir }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

foreach ($a in $arches) {
    Step "Building $a"
    $pub = Join-Path $root "build\store-publish-$a"
    if (Test-Path $pub) { Remove-Item -Recurse -Force -LiteralPath $pub }

    & $dotnet publish $project -c Release -r "win-$a" --self-contained true -p:Platform=$a -o $pub --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $a." }

    # Stage a per-architecture manifest (set ProcessorArchitecture + version).
    [xml]$mm = Get-Content $manifest
    $mm.Package.Identity.SetAttribute('ProcessorArchitecture', $a)
    $mm.Package.Identity.Version = $Version
    $mm.Save((Join-Path $pub 'AppxManifest.xml'))

    $pkg = Join-Path $stageDir "PlaygamaBridge_$a.msix"
    & $makeappx pack /d $pub /p $pkg /o
    if ($LASTEXITCODE -ne 0) { throw "Packaging failed for $a." }
    Ok "Packed $pkg"
}

# ---- Bundle ----------------------------------------------------------------
Step "Creating .msixbundle"
$bundle = Join-Path $outDir ("PlaygamaBridgeMicrosoftStore_{0}.msixbundle" -f $Version)
if (Test-Path $bundle) { Remove-Item -Force -LiteralPath $bundle }
& $makeappx bundle /d $stageDir /p $bundle /bv $Version /o
if ($LASTEXITCODE -ne 0) { throw "Bundling failed." }

Step "DONE - upload this to Partner Center (the Store signs it):"
Ok $bundle
Write-Host ""
Warn "This bundle is UNSIGNED on purpose - the Microsoft Store re-signs it."
Warn "It cannot be installed locally. For local testing use 'Build MSIX.bat'."

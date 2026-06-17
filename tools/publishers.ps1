# Shared helpers for reading publishers.json and applying a profile to the manifest/appsettings.

function Get-PublisherProfile([string]$root, [string]$name) {
    $file = Join-Path $root 'publishers.json'
    if (-not (Test-Path $file)) {
        throw "publishers.json not found. Copy publishers.sample.json to publishers.json and fill it in."
    }
    $list = Get-Content $file -Raw | ConvertFrom-Json
    $p = $list | Where-Object { $_.name -eq $name }
    if (-not $p) { throw "Publisher '$name' not found in publishers.json." }
    return $p
}

# Writes Identity (Name/Publisher) + PublisherDisplayName into the manifest, and clientId /
# serviceTicketBaseUrl into appsettings.json. Returns the resolved pfx info via the pipeline.
function Set-PublisherInfo([object]$p, [string]$root, [string]$manifestPath, [string]$appSettingsPath) {
    [xml]$m = Get-Content $manifestPath
    if ($p.identityName) { $m.Package.Identity.Name = [string]$p.identityName }
    if ($p.publisher) { $m.Package.Identity.Publisher = [string]$p.publisher }
    if ($p.publisherDisplayName) { $m.Package.Properties.PublisherDisplayName = [string]$p.publisherDisplayName }
    $m.Save($manifestPath)

    if ($appSettingsPath) {
        $url = if ($p.serviceTicketBaseUrl) { [string]$p.serviceTicketBaseUrl } else { 'https://playgama.com' }
        $cfg = [ordered]@{ clientId = [string]$p.clientId; serviceTicketBaseUrl = $url }
        ($cfg | ConvertTo-Json) | Set-Content -Path $appSettingsPath -Encoding UTF8
    }

    $pfxPath = $null
    if ($p.pfx) {
        $pfxPath = if ([System.IO.Path]::IsPathRooted([string]$p.pfx)) { [string]$p.pfx } else { Join-Path $root ([string]$p.pfx) }
    }
    return [pscustomobject]@{ PfxPath = $pfxPath; PfxPassword = [string]$p.pfxPassword }
}

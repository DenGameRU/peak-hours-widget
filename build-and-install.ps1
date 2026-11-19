<#
.SYNOPSIS
    Builds, signs, sideloads the Peak Hours Windows 11 widget, and pins-ready installs it.
.DESCRIPTION
    1. Creates/reuses a self-signed code-signing certificate (CN=PeakHoursWidget).
    2. Trusts that certificate machine-wide (requires Administrator).
    3. Publishes a signed MSIX with single-project MSIX tooling.
    4. Installs the MSIX with Add-AppxPackage.
.PARAMETER NoInstall
    Build and sign only, do not install.
.PARAMETER Platform
    x64 by default. Use ARM64 or x86 if you changed RuntimeIdentifiers in the csproj.
#>
[CmdletBinding()]
param(
    [switch]$NoInstall,
    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\PeakHoursWidget\PeakHoursWidget.csproj'
$artifactDir = Join-Path $root 'artifacts'
$certSubject = 'CN=PeakHoursWidget'
$certStore = 'Cert:\CurrentUser\My'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $NoInstall) {
    throw 'Run this script as Administrator: the self-signed cert must be trusted machine-wide to sideload the MSIX.'
}

# 1. Self-signed certificate matching <Identity Publisher> in Package.appxmanifest.
$cert = Get-ChildItem $certStore | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating self-signed certificate $certSubject ..."
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $certSubject `
        -CertStoreLocation $certStore `
        -FriendlyName 'Peak Hours Widget (dev)' `
        -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(3)
}

# 2. Trust it so Windows allows sideloading.
$cerPath = Join-Path $artifactDir 'PeakHoursWidget.cer'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
if ($isAdmin) {
    Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
}
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
Write-Host "Certificate trusted: $($cert.Thumbprint)"

# 3. Publish + sign the MSIX.
Write-Host 'Publishing MSIX ...'
$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', "win-$($Platform.ToLowerInvariant())",
    '--self-contained', 'false',
    "-p:Platform=$Platform",
    '-p:PublishAppxPackage=true',
    "-p:AppxPackageDir=$artifactDir\",
    '-p:AppxPackageSigningEnabled=true',
    "-p:PackageCertificateThumbprint=$($cert.Thumbprint)"
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$msix = Get-ChildItem -Path $artifactDir -Recurse -Filter '*.msix' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $msix) {
    throw 'MSIX was not generated.'
}
Write-Host "Package: $($msix.FullName)"

if ($NoInstall) {
    Write-Host 'Skipping installation (-NoInstall).'
    return
}

# 4. Install / update.
$existing = Get-AppxPackage -Name 'PeakHoursWidget' -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing previously installed package $($existing.PackageFullName) ..."
    Remove-AppxPackage -Package $existing.PackageFullName
}

Write-Host 'Installing package ...'
Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown

Write-Host @'

Installed. One-time step inside Windows:
  1. Press Win + W to open the Widgets panel.
  2. Click "Add widgets".
  3. Find "Peak Hours (local time)" and press Pin.
  4. Now Win + W shows the widget with your local timezone.
'@

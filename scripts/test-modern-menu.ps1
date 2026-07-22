[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$ModernLog = Join-Path $LogDir 'modern-menu.log'
$AppKey = 'HKCU:\Software\PdfRightClickSuite'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Test-CertificateStore {
    param(
        [string]$Thumbprint,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.StoreName]$StoreName,
        [System.Security.Cryptography.X509Certificates.StoreLocation]$StoreLocation = [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser
    )

    if (-not $Thumbprint) {
        return $false
    }

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        return $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Thumbprint, $false).Count -gt 0
    }
    finally {
        $store.Close()
    }
}

if (-not $InstallDir) {
    if (Test-Path -LiteralPath $AppKey) {
        $InstallDir = (Get-ItemProperty -Path $AppKey -Name InstallDir -ErrorAction SilentlyContinue).InstallDir
    }
    if (-not $InstallDir) {
        $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\PdfRightClickSuite'
    }
}

if (-not $PackagePath) {
    $PackagePath = Join-Path $InstallDir 'modern\PdfRightClickSuite.sparse.msix'
    if (-not (Test-Path -LiteralPath $PackagePath)) {
        $PackagePath = Join-Path $RepoRoot 'artifacts\modern\PdfRightClickSuite.sparse.msix'
    }
}

$state = if (Test-Path -LiteralPath $AppKey) { Get-ItemProperty -Path $AppKey -ErrorAction SilentlyContinue } else { $null }
$thumbprint = if ($state) { $state.ModernCertificateThumbprint } else { $null }
$package = Get-AppxPackage -Name 'PdfRightClickSuite' -ErrorAction SilentlyContinue
$result = [pscustomobject]@{
    Windows11OrNewer = [Environment]::OSVersion.Version.Build -ge 22000
    InstallDirExists = Test-Path -LiteralPath $InstallDir
    PackagePath = $PackagePath
    PackageFileExists = Test-Path -LiteralPath $PackagePath
    ModernPackageRegistered = [bool]$package
    PackageFullName = if ($package) { $package.PackageFullName } else { $null }
    CertificateThumbprint = $thumbprint
    CertificateTrustedPeople = Test-CertificateStore -Thumbprint $thumbprint -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople)
    CertificateRoot = Test-CertificateStore -Thumbprint $thumbprint -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::Root)
    CertificateMachineTrustedPeople = Test-CertificateStore -Thumbprint $thumbprint -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople) -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    ClassicContextHandler = Test-Path -LiteralPath 'HKCU:\Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite'
    ModernLogPath = $ModernLog
}

$result | Format-List
$result | ConvertTo-Json | Add-Content -LiteralPath $ModernLog

if (-not $result.ModernPackageRegistered) {
    exit 1
}

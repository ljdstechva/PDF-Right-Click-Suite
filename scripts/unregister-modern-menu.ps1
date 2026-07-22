[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$WhatIfOnly,
    [switch]$KeepCertificate
)

$ErrorActionPreference = 'Stop'
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$ModernLog = Join-Path $LogDir 'modern-menu.log'
$UninstallLog = Join-Path $LogDir 'uninstall.log'
$AppKey = 'HKCU:\Software\PdfRightClickSuite'
$PackageName = 'PdfRightClickSuite'

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Write-ModernLog {
    param([string]$Message)
    $line = "$(Get-Date -Format o) $Message"
    Add-Content -LiteralPath $ModernLog -Value $line
    Add-Content -LiteralPath $UninstallLog -Value $line
}

function Remove-CertificateIfScriptOwned {
    param(
        [string]$Thumbprint,
        [string]$Subject,
        [string]$ImportedFlagName,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.StoreName]$StoreName,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.StoreLocation]$StoreLocation
    )

    if (-not $Thumbprint -or $KeepCertificate) {
        return
    }

    $imported = $false
    if (Test-Path -LiteralPath $AppKey) {
        $value = (Get-ItemProperty -Path $AppKey -Name $ImportedFlagName -ErrorAction SilentlyContinue).$ImportedFlagName
        $imported = $value -eq $true -or $value -eq 'True'
    }

    if (-not $imported) {
        Write-ModernLog "Leaving $StoreLocation\$StoreName certificate in place; it was not recorded as script-owned."
        return
    }

    if ($StoreLocation -eq [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine -and -not (Test-IsAdministrator)) {
        Write-ModernLog "Leaving $StoreLocation\$StoreName certificate in place; removing script-owned machine certificates requires elevated PowerShell."
        return
    }

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, $StoreLocation)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $matches = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Thumbprint, $false)
        foreach ($certificate in $matches) {
            if ($certificate.Subject -like '*PdfRightClickSuite*' -and ($Subject -eq $null -or $certificate.Subject -eq $Subject)) {
                if (-not $WhatIfOnly) {
                    $store.Remove($certificate)
                }

                Write-ModernLog "Removed script-owned PdfRightClickSuite certificate from $StoreLocation\$StoreName thumbprint=$Thumbprint"
            }
            else {
                Write-ModernLog "Refused to remove non-matching certificate from $StoreLocation\$StoreName thumbprint=$Thumbprint subject='$($certificate.Subject)'"
            }
        }
    }
    finally {
        $store.Close()
    }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-ShellAssociationChanged {
    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotifyModernUninstall -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotifyModernUninstall]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
}

$thumbprint = $null
$subject = $null
if (Test-Path -LiteralPath $AppKey) {
    $state = Get-ItemProperty -Path $AppKey -ErrorAction SilentlyContinue
    $thumbprint = $state.ModernCertificateThumbprint
    $subject = $state.ModernCertificateSubject
}

$packages = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if (-not $packages) {
    Write-ModernLog 'No PdfRightClickSuite modern package registration was found.'
}

foreach ($package in $packages) {
    Write-ModernLog "Removing package '$($package.PackageFullName)'"
    if ($WhatIfOnly) {
        Write-Host "Dry run: Remove-AppxPackage $($package.PackageFullName)"
    }
    else {
        Remove-AppxPackage -Package $package.PackageFullName
    }
}

Remove-CertificateIfScriptOwned -Thumbprint $thumbprint -Subject $subject -ImportedFlagName 'ModernCertificateTrustedPeopleImportedByScript' -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople) -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
Remove-CertificateIfScriptOwned -Thumbprint $thumbprint -Subject $subject -ImportedFlagName 'ModernCertificateRootImportedByScript' -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::Root) -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
Remove-CertificateIfScriptOwned -Thumbprint $thumbprint -Subject $subject -ImportedFlagName 'ModernCertificateMachineTrustedPeopleImportedByScript' -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople) -StoreLocation ([System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)

if (-not $WhatIfOnly -and (Test-Path -LiteralPath $AppKey)) {
    foreach ($name in @(
        'ModernCertificateThumbprint',
        'ModernCertificateSubject',
        'ModernCertificateTrustedPeopleImportedByScript',
        'ModernCertificateRootImportedByScript',
        'ModernCertificateMachineTrustedPeopleImportedByScript',
        'ModernPackageFullName',
        'ModernPackagePublisher')) {
        Remove-ItemProperty -Path $AppKey -Name $name -ErrorAction SilentlyContinue
    }
}

Invoke-ShellAssociationChanged
Write-ModernLog 'Modern package unregister flow completed.'
Write-Host 'PdfRightClickSuite modern context-menu package registration removed or was not present.'

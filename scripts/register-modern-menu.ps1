[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$PackagePath,
    [switch]$WhatIfOnly,
    [switch]$NoRootRetry
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$ModernLog = Join-Path $LogDir 'modern-menu.log'
$InstallLog = Join-Path $LogDir 'install.log'
$AppKey = 'HKCU:\Software\PdfRightClickSuite'
$PackageName = 'PdfRightClickSuite'

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Write-ModernLog {
    param([string]$Message)
    $line = "$(Get-Date -Format o) $Message"
    Add-Content -LiteralPath $ModernLog -Value $line
    Add-Content -LiteralPath $InstallLog -Value $line
}

function Resolve-InstallDir {
    if ($InstallDir) {
        return [System.IO.Path]::GetFullPath($InstallDir)
    }

    if (Test-Path -LiteralPath $AppKey) {
        $registered = (Get-ItemProperty -Path $AppKey -Name InstallDir -ErrorAction SilentlyContinue).InstallDir
        if ($registered) {
            return [System.IO.Path]::GetFullPath($registered)
        }
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'artifacts\release\app'))
}

function Resolve-PackagePath {
    param([string]$ResolvedInstallDir)

    $candidates = @()
    if ($PackagePath) {
        $candidates += $PackagePath
    }

    $candidates += Join-Path $ResolvedInstallDir 'modern\PdfRightClickSuite.sparse.msix'
    $candidates += Join-Path $RepoRoot 'artifacts\modern\PdfRightClickSuite.sparse.msix'
    $candidates += Join-Path $RepoRoot 'artifacts\release\modern\PdfRightClickSuite.sparse.msix'

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw "Sparse MSIX package was not found. Checked: $($candidates -join '; ')"
}

function Get-PackageCertificate {
    param([string]$ResolvedPackagePath)

    $signature = Get-AuthenticodeSignature -LiteralPath $ResolvedPackagePath
    if (-not $signature.SignerCertificate) {
        throw "Sparse MSIX package is not signed or the signer certificate could not be read: $ResolvedPackagePath"
    }

    if ($signature.SignerCertificate.Subject -notlike '*PdfRightClickSuite*') {
        throw "Refusing to trust unexpected package certificate subject '$($signature.SignerCertificate.Subject)'."
    }

    return $signature.SignerCertificate
}

function Ensure-CertificateInStore {
    param(
        [Parameter(Mandatory = $true)]$Certificate,
        [Parameter(Mandatory = $true)][System.Security.Cryptography.X509Certificates.StoreName]$StoreName
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new($StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try {
        $matches = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Certificate.Thumbprint, $false)
        if ($matches.Count -gt 0) {
            Write-ModernLog "Certificate already trusted in CurrentUser\$StoreName thumbprint=$($Certificate.Thumbprint)"
            return $false
        }

        if ($StoreName -eq [System.Security.Cryptography.X509Certificates.StoreName]::Root) {
            if (-not $WhatIfOnly) {
                $tempCert = Join-Path $env:TEMP "PdfRightClickSuite-$($Certificate.Thumbprint).cer"
                Export-Certificate -Cert $Certificate -FilePath $tempCert -Force | Out-Null
                try {
                    $output = & certutil.exe -user -addstore Root $tempCert 2>&1
                    $output | ForEach-Object { Write-ModernLog "certutil Root import: $_" }
                    if ($LASTEXITCODE -ne 0) {
                        throw "certutil -user -addstore Root failed with exit code $LASTEXITCODE."
                    }
                }
                finally {
                    Remove-Item -LiteralPath $tempCert -Force -ErrorAction SilentlyContinue
                }
            }

            Write-ModernLog "Added package certificate to CurrentUser\$StoreName thumbprint=$($Certificate.Thumbprint)"
            return $true
        }

        if (-not $WhatIfOnly) {
            $store.Add($Certificate)
        }

        Write-ModernLog "Added package certificate to CurrentUser\$StoreName thumbprint=$($Certificate.Thumbprint)"
        return $true
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

function Ensure-CertificateInMachineTrustedPeople {
    param([Parameter(Mandatory = $true)]$Certificate)

    if (-not (Test-IsAdministrator)) {
        $message = 'CurrentUser certificate trust was not sufficient for Add-AppxPackage on this Windows build, and this process is not elevated. Modern menu registration requires importing the PdfRightClickSuite signing certificate into LocalMachine\TrustedPeople from an elevated installer or elevated PowerShell.'
        Write-ModernLog $message
        throw $message
    }

    $tempCert = Join-Path $env:TEMP "PdfRightClickSuite-machine-$($Certificate.Thumbprint).cer"
    Export-Certificate -Cert $Certificate -FilePath $tempCert -Force | Out-Null
    try {
        $output = & certutil.exe -addstore TrustedPeople $tempCert 2>&1
        $output | ForEach-Object { Write-ModernLog "certutil LocalMachine TrustedPeople import: $_" }
        if ($LASTEXITCODE -ne 0) {
            throw "certutil -addstore TrustedPeople failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempCert -Force -ErrorAction SilentlyContinue
    }

    Write-ModernLog "Added package certificate to LocalMachine\TrustedPeople thumbprint=$($Certificate.Thumbprint)"
    return $true
}

function Save-ModernRegistrationState {
    param(
        [Parameter(Mandatory = $true)]$Certificate,
        [string]$PackageFullName,
        [bool]$TrustedPeopleImported,
        [bool]$RootImported,
        [bool]$MachineTrustedPeopleImported
    )

    if ($WhatIfOnly) {
        return
    }

    if (-not (Test-Path -LiteralPath $AppKey)) {
        New-Item -Path $AppKey -Force | Out-Null
    }
    Set-ItemProperty -Path $AppKey -Name ModernCertificateThumbprint -Value $Certificate.Thumbprint
    Set-ItemProperty -Path $AppKey -Name ModernCertificateSubject -Value $Certificate.Subject
    Set-ItemProperty -Path $AppKey -Name ModernCertificateTrustedPeopleImportedByScript -Value $TrustedPeopleImported
    Set-ItemProperty -Path $AppKey -Name ModernCertificateRootImportedByScript -Value $RootImported
    Set-ItemProperty -Path $AppKey -Name ModernCertificateMachineTrustedPeopleImportedByScript -Value $MachineTrustedPeopleImported
    if ($PackageFullName) {
        Set-ItemProperty -Path $AppKey -Name ModernPackageFullName -Value $PackageFullName
    }

    Set-ItemProperty -Path $AppKey -Name ModernPackagePublisher -Value $Certificate.Subject
}

function Invoke-ShellAssociationChanged {
    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotifyModern -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotifyModern]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
}

$resolvedInstallDir = Resolve-InstallDir
if (-not (Test-Path -LiteralPath $resolvedInstallDir)) {
    throw "Install directory was not found: $resolvedInstallDir"
}

$resolvedPackagePath = Resolve-PackagePath -ResolvedInstallDir $resolvedInstallDir
$certificate = Get-PackageCertificate -ResolvedPackagePath $resolvedPackagePath
Write-ModernLog "Modern registration starting package='$resolvedPackagePath' externalLocation='$resolvedInstallDir' thumbprint=$($certificate.Thumbprint)"

$existing = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if ($existing) {
    Write-ModernLog "Modern package already registered: $($existing.PackageFullName)"
    Save-ModernRegistrationState -Certificate $certificate -PackageFullName $existing.PackageFullName -TrustedPeopleImported $false -RootImported $false -MachineTrustedPeopleImported $false
    Write-Host "PdfRightClickSuite modern package already registered: $($existing.PackageFullName)"
    exit 0
}

$trustedPeopleImported = Ensure-CertificateInStore -Certificate $certificate -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::TrustedPeople)
$rootImported = $false
$machineTrustedPeopleImported = $false

if ($WhatIfOnly) {
    Write-Host "Dry run: Add-AppxPackage -Path `"$resolvedPackagePath`" -ExternalLocation `"$resolvedInstallDir`""
    Save-ModernRegistrationState -Certificate $certificate -PackageFullName '' -TrustedPeopleImported $trustedPeopleImported -RootImported $rootImported -MachineTrustedPeopleImported $machineTrustedPeopleImported
    exit 0
}

try {
    Add-AppxPackage -Path $resolvedPackagePath -ExternalLocation $resolvedInstallDir
}
catch {
    $message = $_.Exception.Message
    Write-ModernLog "Add-AppxPackage first attempt failed: $message"
    if ($NoRootRetry -or $message -notmatch '0x800B0109|certificate.*not trusted|root certificate') {
        throw
    }

    $rootImported = Ensure-CertificateInStore -Certificate $certificate -StoreName ([System.Security.Cryptography.X509Certificates.StoreName]::Root)
    Save-ModernRegistrationState -Certificate $certificate -PackageFullName '' -TrustedPeopleImported $trustedPeopleImported -RootImported $rootImported -MachineTrustedPeopleImported $machineTrustedPeopleImported
    Write-ModernLog 'Retrying Add-AppxPackage after CurrentUser\Root trust import.'
    try {
        Add-AppxPackage -Path $resolvedPackagePath -ExternalLocation $resolvedInstallDir
    }
    catch {
        $retryMessage = $_.Exception.Message
        Write-ModernLog "Add-AppxPackage after CurrentUser root failed: $retryMessage"
        if ($retryMessage -notmatch '0x800B0109|certificate.*not trusted|root certificate') {
            throw
        }

        $machineTrustedPeopleImported = Ensure-CertificateInMachineTrustedPeople -Certificate $certificate
        Save-ModernRegistrationState -Certificate $certificate -PackageFullName '' -TrustedPeopleImported $trustedPeopleImported -RootImported $rootImported -MachineTrustedPeopleImported $machineTrustedPeopleImported
        Write-ModernLog 'Retrying Add-AppxPackage after LocalMachine\TrustedPeople trust import.'
        Add-AppxPackage -Path $resolvedPackagePath -ExternalLocation $resolvedInstallDir
    }
}

$registered = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if (-not $registered) {
    throw 'Add-AppxPackage completed but Get-AppxPackage did not return PdfRightClickSuite.'
}

Save-ModernRegistrationState -Certificate $certificate -PackageFullName $registered.PackageFullName -TrustedPeopleImported $trustedPeopleImported -RootImported $rootImported -MachineTrustedPeopleImported $machineTrustedPeopleImported
Invoke-ShellAssociationChanged
Write-ModernLog "Modern package registered: $($registered.PackageFullName)"
Write-Host "PdfRightClickSuite modern package registered: $($registered.PackageFullName)"

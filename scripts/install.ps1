[CmdletBinding()]
param(
    [string]$SourcePath,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\PdfRightClickSuite'),
    [switch]$RestartExplorer,
    [switch]$NoRestartPrompt,
    [switch]$SkipPdfGearDisable
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$UsingDefaultSource = -not $PSBoundParameters.ContainsKey('SourcePath')
$Clsid = '{68A2F5F6-2E91-4C66-B126-896B8C6C6834}'
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$InstallLog = Join-Path $LogDir 'install.log'
if (-not $SourcePath) {
    $SourcePath = Join-Path $RepoRoot 'artifacts\release\app'
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Write-InstallLog {
    param([string]$Message)
    Add-Content -LiteralPath $InstallLog -Value "$(Get-Date -Format o) $Message"
}

function PathEquals {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right
    )

    $leftFull = [System.IO.Path]::GetFullPath($Left).TrimEnd('\', '/')
    $rightFull = [System.IO.Path]::GetFullPath($Right).TrimEnd('\', '/')
    return [System.String]::Equals($leftFull, $rightFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function PathIsUnder {
    param(
        [Parameter(Mandatory = $true)][string]$Child,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $childFull = [System.IO.Path]::GetFullPath($Child).TrimEnd('\', '/')
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    return [System.String]::Equals($childFull, $parentFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $childFull.StartsWith($parentFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-ShellAssociationChanged {
    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotify -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotify]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Test-ReleaseReady {
    return (Test-Path -LiteralPath (Join-Path $SourcePath 'PdfRightClickSuite.Cli.exe')) -and
        (Test-Path -LiteralPath (Join-Path $SourcePath 'PdfRightClickSuite.ShellExtension.dll'))
}

function Ensure-ReleaseReady {
    if ((Test-ReleaseReady)) {
        return
    }

    if ($UsingDefaultSource) {
        $buildScript = Join-Path $PSScriptRoot 'build-release.ps1'
        Write-Host "Release binaries are missing; running $buildScript"
        & $buildScript
        if ($LASTEXITCODE -ne 0) {
            throw "build-release.ps1 failed with exit code ${LASTEXITCODE}."
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $SourcePath 'PdfRightClickSuite.ShellExtension.dll'))) {
        throw "Shell extension DLL was not found in '$SourcePath'. Build native release with Visual Studio C++ Build Tools before installing."
    }

    if (-not (Test-Path -LiteralPath (Join-Path $SourcePath 'PdfRightClickSuite.Cli.exe'))) {
        throw "CLI executable was not found in '$SourcePath'. Run scripts\build-release.ps1 first."
    }
}

function Restart-ExplorerIfRequested {
    if ($RestartExplorer) {
        Stop-Process -Name explorer -Force
        Start-Process explorer.exe
        return
    }

    if (-not $NoRestartPrompt) {
        $answer = Read-Host 'Restart Explorer now so the context menu reloads? [y/N]'
        if ($answer -match '^(y|yes)$') {
            Stop-Process -Name explorer -Force
            Start-Process explorer.exe
        }
        else {
            Write-Host 'Manual restart command: Stop-Process -Name explorer -Force; Start-Process explorer.exe'
        }
    }
}

function Disable-PdfGearContextMenuIfAvailable {
    if ($SkipPdfGearDisable) {
        Write-InstallLog 'Skipping PDF Gear context-menu disable because -SkipPdfGearDisable was specified.'
        return
    }

    $scriptCandidates = @(
        (Join-Path $InstallDir 'scripts\disable-pdfgear-context-menu.ps1'),
        (Join-Path $PSScriptRoot 'disable-pdfgear-context-menu.ps1')
    )
    $disableScript = $scriptCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $disableScript) {
        Write-InstallLog 'PDF Gear context-menu disable script was not found; continuing classic install.'
        return
    }

    $backupRoot = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\registry-backups'
    try {
        Write-InstallLog "Disabling PDF Gear context-menu entries using '$disableScript'"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $disableScript -BackupRoot $backupRoot -NoShellRefresh
        if ($LASTEXITCODE -ne 0) {
            throw "disable-pdfgear-context-menu.ps1 exited with code $LASTEXITCODE."
        }

        Write-InstallLog "PDF Gear context-menu disable completed. Backup root: $backupRoot"
    }
    catch {
        Write-InstallLog "PDF Gear context-menu disable failed; continuing classic install: $($_.Exception.Message)"
        Write-Warning "PDF Gear context-menu disable failed; continuing classic install. $($_.Exception.Message)"
    }
}

function Register-ClassicTopMenu {
    $scriptCandidates = @(
        (Join-Path $InstallDir 'scripts\install-classic-top-menu.ps1'),
        (Join-Path $PSScriptRoot 'install-classic-top-menu.ps1')
    )
    $classicTopScript = $scriptCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $classicTopScript) {
        throw 'install-classic-top-menu.ps1 was not found; cannot register the top-position classic PDF menu.'
    }

    $backupRoot = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\registry-backups\classic-top-menu'
    Write-InstallLog "Registering top-position classic PDF menu using '$classicTopScript'"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $classicTopScript -InstallDir $InstallDir -BackupRoot $backupRoot -NoShellRefresh
    if ($LASTEXITCODE -ne 0) {
        throw "install-classic-top-menu.ps1 exited with code $LASTEXITCODE."
    }

    Write-InstallLog "Top-position classic PDF menu registration completed. Backup root: $backupRoot"
}

function Remove-StaleModernMenuInstallArtifacts {
    $relativePaths = @(
        'modern',
        'scripts\install-modern.ps1',
        'scripts\uninstall-modern.ps1',
        'scripts\register-modern-menu.ps1',
        'scripts\register-modern-menu-elevated.ps1',
        'scripts\unregister-modern-menu.ps1',
        'scripts\test-modern-menu.ps1',
        'scripts\build-release.ps1'
    )

    foreach ($relativePath in $relativePaths) {
        $path = Join-Path $InstallDir $relativePath
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        if (-not (PathIsUnder -Child $path -Parent $InstallDir)) {
            throw "Refusing to remove stale install artifact outside InstallDir: $path"
        }

        Remove-Item -LiteralPath $path -Recurse -Force
        Write-InstallLog "Removed stale optional modern-menu artifact from install directory: $relativePath"
    }
}

Ensure-ReleaseReady
Write-InstallLog "Installing from '$SourcePath' to '$InstallDir'"

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
if (PathEquals -Left $SourcePath -Right $InstallDir) {
    Write-InstallLog 'SourcePath and InstallDir are the same; skipping file copy.'
}
else {
    Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallDir -Recurse -Force
}

$appKey = 'HKCU:\Software\PdfRightClickSuite'
if (-not (Test-Path -LiteralPath $appKey)) {
    New-Item -Path $appKey -Force | Out-Null
}
Set-ItemProperty -Path $appKey -Name InstallDir -Value $InstallDir

$shellDll = Join-Path $InstallDir 'PdfRightClickSuite.ShellExtension.dll'
$regsvr32 = Join-Path $env:WINDIR 'System32\regsvr32.exe'
$regsvr = Start-Process -FilePath $regsvr32 -ArgumentList @('/s', $shellDll) -Wait -PassThru
if ($regsvr.ExitCode -ne 0) {
    throw "regsvr32 failed with exit code $($regsvr.ExitCode): $shellDll"
}

Register-ClassicTopMenu

$topHandlerKey = 'HKCU:\Software\Classes\*\shell\PdfRightClickSuite'
if (-not (Test-Path -LiteralPath $topHandlerKey)) {
    throw 'Classic top-menu registration did not create the expected per-user shell verb registry key.'
}

$topHandler = Get-ItemProperty -LiteralPath $topHandlerKey
if ($topHandler.Position -ne 'Top' -or $topHandler.ExplorerCommandHandler -ne '{065E1050-7F50-4FDF-94C6-19B998E64A83}') {
    throw 'Classic top-menu registry key is missing Position=Top or the expected ExplorerCommandHandler CLSID.'
}

$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
if (-not (Test-Path -LiteralPath $approvedKey)) {
    New-Item -Path $approvedKey -Force | Out-Null
}
Set-ItemProperty -Path $approvedKey -Name $Clsid -Value 'PdfRightClickSuite PDF context menu'
Set-ItemProperty -Path $approvedKey -Name '{065E1050-7F50-4FDF-94C6-19B998E64A83}' -Value 'PdfRightClickSuite top classic PDF menu'
Write-InstallLog "Registered classic top menu with ExplorerCommandHandler {065E1050-7F50-4FDF-94C6-19B998E64A83}; IContextMenu CLSID $Clsid remains fallback-only."
Write-InstallLog 'Modern AppX/MSIX context-menu registration is intentionally disabled in the default installer flow.'
Remove-StaleModernMenuInstallArtifacts
Disable-PdfGearContextMenuIfAvailable
Invoke-ShellAssociationChanged
Write-InstallLog 'Called SHChangeNotify SHCNE_ASSOCCHANGED'

Write-Host "Installed PdfRightClickSuite to $InstallDir"
Write-Host 'If the PDF menu does not appear immediately, restart Explorer.'
Restart-ExplorerIfRequested

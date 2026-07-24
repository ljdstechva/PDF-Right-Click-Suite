[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$RestartExplorer,
    [switch]$NoRestartPrompt,
    [switch]$RemoveLogs
)

$ErrorActionPreference = 'Stop'
$Clsid = '{68A2F5F6-2E91-4C66-B126-896B8C6C6834}'
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$UninstallLog = Join-Path $LogDir 'uninstall.log'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Write-UninstallLog {
    param([string]$Message)
    Add-Content -LiteralPath $UninstallLog -Value "$(Get-Date -Format o) $Message"
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

if (-not $InstallDir) {
    $appKey = 'HKCU:\Software\PdfRightClickSuite'
    if (Test-Path -LiteralPath $appKey) {
        $InstallDir = (Get-ItemProperty -Path $appKey -Name InstallDir -ErrorAction SilentlyContinue).InstallDir
    }

    if (-not $InstallDir) {
        $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\PdfRightClickSuite'
    }
}

function Restart-ExplorerIfRequested {
    if ($RestartExplorer) {
        Stop-Process -Name explorer -Force
        Start-Process explorer.exe
        return
    }

    if (-not $NoRestartPrompt) {
        $answer = Read-Host 'Restart Explorer now so the context menu unloads? [y/N]'
        if ($answer -match '^(y|yes)$') {
            Stop-Process -Name explorer -Force
            Start-Process explorer.exe
        }
        else {
            Write-Host 'Manual restart command: Stop-Process -Name explorer -Force; Start-Process explorer.exe'
        }
    }
}

function Unregister-ClassicTopMenuIfAvailable {
    $scriptCandidates = @(
        (Join-Path $InstallDir 'scripts\uninstall-classic-top-menu.ps1'),
        (Join-Path $PSScriptRoot 'uninstall-classic-top-menu.ps1')
    )
    $classicTopScript = $scriptCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $classicTopScript) {
        Write-UninstallLog 'uninstall-classic-top-menu.ps1 was not found; removing known classic top-menu keys inline.'
        return
    }

    $backupRoot = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\registry-backups\classic-top-menu'
    Write-UninstallLog "Unregistering top-position classic PDF menu using '$classicTopScript'"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $classicTopScript -BackupRoot $backupRoot -NoShellRefresh
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "uninstall-classic-top-menu.ps1 exited with code $LASTEXITCODE."
    }
}

Write-UninstallLog 'Modern AppX/MSIX context-menu unregister is intentionally not part of the default classic uninstall flow.'
Unregister-ClassicTopMenuIfAvailable

$shellDll = Join-Path $InstallDir 'PdfRightClickSuite.ShellExtension.dll'
if (Test-Path -LiteralPath $shellDll) {
    $regsvr32 = Join-Path $env:WINDIR 'System32\regsvr32.exe'
    $regsvr = Start-Process -FilePath $regsvr32 -ArgumentList @('/u', '/s', $shellDll) -Wait -PassThru
    if ($regsvr.ExitCode -ne 0) {
        Write-Warning "regsvr32 unregister failed with exit code $($regsvr.ExitCode): $shellDll"
    }
}

Write-UninstallLog "Unregistering from '$InstallDir'"
Remove-Item -LiteralPath 'HKCU:\Software\Classes\*\shell\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\AllFilesystemObjects\shell\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\SystemFileAssociations\image\shell\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\SystemFileAssociations\text\shell\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{68A2F5F6-2E91-4C66-B126-896B8C6C6834}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{065E1050-7F50-4FDF-94C6-19B998E64A83}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{AD6102B8-2161-44C7-B63A-E93821D6FBC0}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{4AA1C5C6-946D-4268-AF0C-8C3C137B0E24}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{8EA50A51-83A3-453F-8007-C946A13B081F}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{388E7AA8-AEDA-42C5-9477-0B50F86D4A6C}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Classes\CLSID\{EF7E97A8-DC06-4309-BCC9-48CA62875387}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\PdfRightClickSuite' -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved' -Name $Clsid -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved' -Name '{065E1050-7F50-4FDF-94C6-19B998E64A83}' -ErrorAction SilentlyContinue
Invoke-ShellAssociationChanged
Write-UninstallLog 'Called SHChangeNotify SHCNE_ASSOCCHANGED'

if (Test-Path -LiteralPath $InstallDir) {
    $fullInstall = [System.IO.Path]::GetFullPath($InstallDir)
    $programsRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
    if (-not $fullInstall.StartsWith($programsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove install directory outside LocalAppData Programs: $fullInstall"
    }

    $scriptPath = $PSCommandPath
    if ($scriptPath -and (PathIsUnder -Child $scriptPath -Parent $InstallDir)) {
        Write-UninstallLog 'Skipping install directory removal because uninstall.ps1 is running from inside InstallDir; installer uninstaller will remove files.'
    }
    else {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
    }
}

$logDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
if (Test-Path -LiteralPath $logDir) {
    $deleteLogs = $RemoveLogs
    if (-not $RemoveLogs -and -not $NoRestartPrompt) {
        $answer = Read-Host 'Remove PdfRightClickSuite logs too? [y/N]'
        $deleteLogs = $answer -match '^(y|yes)$'
    }

    if ($deleteLogs) {
        Remove-Item -LiteralPath $logDir -Recurse -Force
    }
}

Write-Host 'PdfRightClickSuite uninstalled.'
Restart-ExplorerIfRequested

[CmdletBinding()]
param(
    [string]$BackupRoot = (Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\registry-backups\classic-top-menu'),
    [switch]$NoShellRefresh
)

$ErrorActionPreference = 'Stop'

$TopClsid = '{065E1050-7F50-4FDF-94C6-19B998E64A83}'
$FallbackClsid = '{68A2F5F6-2E91-4C66-B126-896B8C6C6834}'
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$LogPath = Join-Path $LogDir 'classic-top-menu-uninstall.log'

New-Item -ItemType Directory -Force -Path $LogDir, $BackupRoot | Out-Null

function Write-ClassicTopLog {
    param([string]$Message)
    Add-Content -LiteralPath $LogPath -Value "$(Get-Date -Format o) $Message"
}

function ConvertTo-RegExePath {
    param([Parameter(Mandatory = $true)][string]$PowerShellPath)

    if ($PowerShellPath.StartsWith('HKCU:\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'HKCU\' + $PowerShellPath.Substring(6)
    }

    throw "Unsupported registry path: $PowerShellPath"
}

function Get-SafeBackupName {
    param([Parameter(Mandatory = $true)][string]$RegPath)

    $safe = $RegPath -replace '[\\/:*?"<>|{}]', '_'
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($RegPath))
    }
    finally {
        $sha256.Dispose()
    }
    $hash = (($hashBytes | ForEach-Object { $_.ToString('X2') }) -join '').Substring(0, 12)
    return "$safe-$hash.reg"
}

function Export-RegistryKeyIfPresent {
    param([Parameter(Mandatory = $true)][string]$PowerShellPath)

    $exists = Test-Path -LiteralPath $PowerShellPath
    $result = [ordered]@{
        Path = $PowerShellPath
        Exists = $exists
        Exported = $false
        BackupFile = $null
        Error = $null
    }

    if (-not $exists) {
        return $result
    }

    $regPath = ConvertTo-RegExePath -PowerShellPath $PowerShellPath
    $backupFile = Join-Path $BackupRoot (Get-SafeBackupName -RegPath $regPath)
    $export = & reg.exe export $regPath $backupFile /y 2>&1
    if ($LASTEXITCODE -eq 0) {
        $result.Exported = $true
        $result.BackupFile = $backupFile
    }
    else {
        $result.Error = ($export | Out-String).Trim()
    }

    return $result
}

function Invoke-ShellAssociationChanged {
    if ($NoShellRefresh) {
        return
    }

    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotifyClassicTopUninstall -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotifyClassicTopUninstall]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$targets = @(
    'HKCU:\Software\Classes\*\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\AllFilesystemObjects\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\image\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\text\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite',
    'HKCU:\Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\PdfRightClickSuite'
)

$exports = foreach ($target in $targets) {
    Export-RegistryKeyIfPresent -PowerShellPath $target
}

$removed = foreach ($target in $targets) {
    $wasPresent = Test-Path -LiteralPath $target
    if ($wasPresent) {
        Remove-Item -LiteralPath $target -Recurse -Force
        Write-ClassicTopLog "Removed registry key: $target"
    }

    [ordered]@{
        Path = $target
        Removed = $wasPresent
    }
}

$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
Remove-ItemProperty -Path $approvedKey -Name $TopClsid -ErrorAction SilentlyContinue
Remove-ItemProperty -Path $approvedKey -Name $FallbackClsid -ErrorAction SilentlyContinue

$appKey = 'HKCU:\Software\PdfRightClickSuite'
foreach ($name in @('ClassicTopMenuHandlerType', 'ClassicTopMenuClsid', 'ClassicTopMenuPosition', 'IContextMenuFallbackEnabled')) {
    Remove-ItemProperty -Path $appKey -Name $name -ErrorAction SilentlyContinue
}

Invoke-ShellAssociationChanged

$manifest = [ordered]@{
    CreatedAt = (Get-Date).ToString('o')
    BackupRoot = $BackupRoot
    BackupResults = @($exports)
    RemovedKeys = @($removed)
}

$manifestPath = Join-Path $BackupRoot "classic-top-menu-uninstall-manifest-$timestamp.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-ClassicTopLog "Uninstalled classic top menu. Manifest: $manifestPath"
Write-Host "Unregistered PdfRightClickSuite classic top menu."
Write-Host "Manifest: $manifestPath"

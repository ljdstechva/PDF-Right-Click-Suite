[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\PdfRightClickSuite'),
    [string]$BackupRoot = (Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\registry-backups\classic-top-menu'),
    [switch]$NoShellRefresh
)

$ErrorActionPreference = 'Stop'

$TopClsid = '{065E1050-7F50-4FDF-94C6-19B998E64A83}'
$FallbackClsid = '{68A2F5F6-2E91-4C66-B126-896B8C6C6834}'
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$LogPath = Join-Path $LogDir 'classic-top-menu-install.log'
$TopShellKey = 'HKCU:\Software\Classes\*\shell\PdfRightClickSuite'
$AppKey = 'HKCU:\Software\PdfRightClickSuite'
$PdfIconPath = Join-Path $InstallDir 'assets\pdf.ico'

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

    if ($PowerShellPath.StartsWith('HKLM:\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return 'HKLM\' + $PowerShellPath.Substring(6)
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

function Remove-RegistryKeyIfPresent {
    param([Parameter(Mandatory = $true)][string]$PowerShellPath)

    if (Test-Path -LiteralPath $PowerShellPath) {
        Remove-Item -LiteralPath $PowerShellPath -Recurse -Force
        Write-ClassicTopLog "Removed registry key: $PowerShellPath"
        return $true
    }

    return $false
}

function Ensure-CurrentUserRegistrySubKey {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($RelativePath)
    if ($null -eq $key) {
        throw "Failed to create HKCU:\$RelativePath."
    }

    $key.Dispose()
}

function Invoke-ShellAssociationChanged {
    if ($NoShellRefresh) {
        return
    }

    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotifyClassicTop -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotifyClassicTop]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupTargets = @(
    'HKCU:\Software\Classes\*\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\AllFilesystemObjects\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\image\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\text\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite',
    'HKCU:\Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\PdfRightClickSuite',
    "HKCU:\Software\Classes\CLSID\$TopClsid",
    "HKCU:\Software\Classes\CLSID\$FallbackClsid"
)

$exports = foreach ($target in $backupTargets) {
    Export-RegistryKeyIfPresent -PowerShellPath $target
}

$removed = foreach ($target in $backupTargets | Where-Object { $_ -like '*\shell\PdfRightClickSuite' -or $_ -like '*\ContextMenuHandlers\PdfRightClickSuite' }) {
    [ordered]@{
        Path = $target
        Removed = Remove-RegistryKeyIfPresent -PowerShellPath $target
    }
}

if (-not (Test-Path -LiteralPath $TopShellKey)) {
    Ensure-CurrentUserRegistrySubKey -RelativePath 'Software\Classes\*\shell\PdfRightClickSuite'
}

$iconValue = if (Test-Path -LiteralPath $PdfIconPath) {
    $PdfIconPath
}
else {
    Join-Path $InstallDir 'PdfRightClickSuite.Cli.exe'
}

Set-Item -LiteralPath $TopShellKey -Value ''
Set-ItemProperty -LiteralPath $TopShellKey -Name 'MUIVerb' -Value 'PDF'
Set-ItemProperty -LiteralPath $TopShellKey -Name 'Position' -Value 'Top'
Set-ItemProperty -LiteralPath $TopShellKey -Name 'ExplorerCommandHandler' -Value $TopClsid
Set-ItemProperty -LiteralPath $TopShellKey -Name 'MultiSelectModel' -Value 'Player'
Set-ItemProperty -LiteralPath $TopShellKey -Name 'Icon' -Value $iconValue

$approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved'
if (-not (Test-Path -LiteralPath $approvedKey)) {
    New-Item -Path $approvedKey -Force | Out-Null
}
Set-ItemProperty -Path $approvedKey -Name $TopClsid -Value 'PdfRightClickSuite top classic PDF menu'
Set-ItemProperty -Path $approvedKey -Name $FallbackClsid -Value 'PdfRightClickSuite fallback PDF context menu'

if (-not (Test-Path -LiteralPath $AppKey)) {
    New-Item -Path $AppKey -Force | Out-Null
}
Set-ItemProperty -Path $AppKey -Name 'InstallDir' -Value $InstallDir
Set-ItemProperty -Path $AppKey -Name 'ClassicTopMenuHandlerType' -Value 'shell verb + ExplorerCommandHandler'
Set-ItemProperty -Path $AppKey -Name 'ClassicTopMenuClsid' -Value $TopClsid
Set-ItemProperty -Path $AppKey -Name 'ClassicTopMenuPosition' -Value 'Top'
Set-ItemProperty -Path $AppKey -Name 'ClassicTopMenuIconPath' -Value $iconValue
Set-ItemProperty -Path $AppKey -Name 'IContextMenuFallbackEnabled' -Value 'False'

Invoke-ShellAssociationChanged

$manifest = [ordered]@{
    CreatedAt = (Get-Date).ToString('o')
    InstallDir = $InstallDir
    BackupRoot = $BackupRoot
    TopShellKey = $TopShellKey
    TopClsid = $TopClsid
    HandlerType = 'shell verb + ExplorerCommandHandler'
    Position = 'Top'
    IconValue = $iconValue
    IconExists = Test-Path -LiteralPath $iconValue
    BackupResults = @($exports)
    RemovedDuplicates = @($removed)
    RegistryValues = [ordered]@{
        MUIVerb = 'PDF'
        Position = 'Top'
        ExplorerCommandHandler = $TopClsid
        MultiSelectModel = 'Player'
        Icon = $iconValue
    }
}

$manifestPath = Join-Path $BackupRoot "classic-top-menu-install-manifest-$timestamp.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-ClassicTopLog "Registered classic top menu. Manifest: $manifestPath"
Write-Host "Registered PdfRightClickSuite classic top menu: $TopShellKey"
Write-Host "Manifest: $manifestPath"

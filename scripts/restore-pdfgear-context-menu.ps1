[CmdletBinding()]
param(
    [string]$ManifestPath,
    [string]$BackupRoot,
    [switch]$DryRun,
    [switch]$NoShellRefresh
)

$ErrorActionPreference = 'Stop'

if (-not $BackupRoot) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (Test-Path -LiteralPath (Join-Path $repoRoot 'PdfRightClickSuite.sln')) {
        $BackupRoot = Join-Path $repoRoot 'artifacts\classic-menu-final\registry-backups'
    }
    else {
        $BackupRoot = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\classic-menu-final\registry-backups'
    }
}

if (-not $ManifestPath) {
    $ManifestPath = Get-ChildItem -LiteralPath $BackupRoot -Filter 'pdfgear-disable-manifest-*.json' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $ManifestPath -or -not (Test-Path -LiteralPath $ManifestPath)) {
    throw "PDF Gear restore manifest was not found. Provide -ManifestPath or restore from: $BackupRoot"
}

function Invoke-ShellAssociationChanged {
    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotifyPdfGearRestore -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotifyPdfGearRestore]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Convert-RegPathToParts {
    param([Parameter(Mandatory = $true)][string]$RegPath)

    $normalized = $RegPath.Trim()
    if ($normalized.StartsWith('HKEY_CURRENT_USER\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            Hive = 'HKCU'
            RootName = 'HKEY_CURRENT_USER'
            SubPath = $normalized.Substring('HKEY_CURRENT_USER\'.Length)
        }
    }

    if ($normalized.StartsWith('HKEY_LOCAL_MACHINE\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            Hive = 'HKLM'
            RootName = 'HKEY_LOCAL_MACHINE'
            SubPath = $normalized.Substring('HKEY_LOCAL_MACHINE\'.Length)
        }
    }

    throw "Unsupported registry path: $RegPath"
}

function Get-RegistryRoot {
    param([Parameter(Mandatory = $true)][string]$Hive)

    if ($Hive -eq 'HKCU') {
        return [Microsoft.Win32.Registry]::CurrentUser
    }

    if ($Hive -eq 'HKLM') {
        return [Microsoft.Win32.Registry]::LocalMachine
    }

    throw "Unsupported hive: $Hive"
}

function Convert-Kind {
    param([string]$Kind)

    if (-not $Kind) {
        return [Microsoft.Win32.RegistryValueKind]::String
    }

    return [System.Enum]::Parse([Microsoft.Win32.RegistryValueKind], $Kind)
}

function Restore-Value {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Key,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$State
    )

    if ($State.Exists) {
        $Key.SetValue($Name, $State.Value, (Convert-Kind -Kind $State.Kind))
    }
    else {
        $Key.DeleteValue($Name, $false)
    }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$results = @()

foreach ($action in $manifest.Actions) {
    if (-not $action.Success) {
        $results += [pscustomobject]@{
            Mode = $action.Mode
            TargetRegPath = $action.TargetRegPath
            Success = $false
            Message = 'Skipped because the original disable action did not succeed.'
        }
        continue
    }

    try {
        if ($action.Mode -eq 'ShellVerb') {
            $parts = Convert-RegPathToParts -RegPath $action.TargetRegPath
            $root = Get-RegistryRoot -Hive $parts.Hive
            $key = $root.OpenSubKey($parts.SubPath, $true)
            if (-not $key) {
                throw "Could not open shell verb key writable: $($action.TargetRegPath)"
            }

            try {
                if (-not $DryRun) {
                    Restore-Value -Key $key -Name 'LegacyDisable' -State $action.PreviousLegacyDisable
                    Restore-Value -Key $key -Name 'ProgrammaticAccessOnly' -State $action.PreviousProgrammaticAccessOnly
                    Restore-Value -Key $key -Name 'PdfRightClickSuiteDisabledContextMenu' -State $action.PreviousMarker
                }
            }
            finally {
                $key.Close()
            }

            $results += [pscustomobject]@{
                Mode = $action.Mode
                TargetRegPath = $action.TargetRegPath
                Success = $true
                DryRun = [bool]$DryRun
                Message = 'Restored shell verb values.'
            }
        }
        elseif ($action.Mode -eq 'ShellexHandler') {
            if (-not (Test-Path -LiteralPath $action.DisabledPsPath)) {
                throw "Disabled shellex handler key was not found: $($action.DisabledRegPath)"
            }

            if (Test-Path -LiteralPath $action.TargetPsPath) {
                throw "Original shellex handler key already exists: $($action.TargetRegPath)"
            }

            if (-not $DryRun) {
                $originalLeaf = Split-Path -Leaf $action.TargetRegPath
                Rename-Item -LiteralPath $action.DisabledPsPath -NewName $originalLeaf -ErrorAction Stop
            }

            $results += [pscustomobject]@{
                Mode = $action.Mode
                TargetRegPath = $action.TargetRegPath
                Success = $true
                DryRun = [bool]$DryRun
                Message = 'Restored shellex ContextMenuHandlers key name.'
            }
        }
        else {
            throw "Unsupported action mode: $($action.Mode)"
        }
    }
    catch {
        $results += [pscustomobject]@{
            Mode = $action.Mode
            TargetRegPath = $action.TargetRegPath
            Success = $false
            DryRun = [bool]$DryRun
            Message = $_.Exception.Message
        }
    }
}

if (-not $NoShellRefresh -and -not $DryRun -and ($results | Where-Object { $_.Success }).Count -gt 0) {
    Invoke-ShellAssociationChanged
}

$restoreReport = Join-Path (Split-Path -Parent $ManifestPath) ("pdfgear-restore-result-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$successCount = @($results | Where-Object { $_.Success }).Count
$failureCount = @($results | Where-Object { -not $_.Success }).Count

[pscustomobject]@{
    CreatedAt = (Get-Date -Format o)
    ManifestPath = $ManifestPath
    DryRun = [bool]$DryRun
    SuccessCount = $successCount
    FailureCount = $failureCount
    Results = $results
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $restoreReport -Encoding UTF8

Write-Host "PDF Gear restore report: $restoreReport"
Write-Host "Restored: $successCount; failed: $failureCount"

if ($failureCount -gt 0) {
    Write-Warning 'One or more PDF Gear context-menu entries could not be restored. See the restore report for details.'
}

[CmdletBinding()]
param(
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

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
$searchTextPath = Join-Path $BackupRoot "pdfgear-registry-search-$timestamp.txt"
$manifestPath = Join-Path $BackupRoot "pdfgear-disable-manifest-$timestamp.json"

$needles = @('PDF Gear', 'PDFGear', 'PDFgear', 'pdfgear')
$searchRoots = @(
    'HKCU\Software\Classes',
    'HKLM\Software\Classes',
    'HKLM\Software\Wow6432Node\Classes',
    'HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions',
    'HKLM\Software\Microsoft\Windows\CurrentVersion\Shell Extensions'
)

function Write-SearchLine {
    param([string]$Message)
    Add-Content -LiteralPath $searchTextPath -Value $Message
}

function Invoke-ShellAssociationChanged {
    Add-Type -Namespace PdfRightClickSuite.Native -Name ShellNotifyPdfGear -MemberDefinition @'
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
    $SHCNE_ASSOCCHANGED = 0x08000000
    $SHCNF_IDLIST = 0x0000
    [PdfRightClickSuite.Native.ShellNotifyPdfGear]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)
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

    if ($normalized.StartsWith('HKCU\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            Hive = 'HKCU'
            RootName = 'HKEY_CURRENT_USER'
            SubPath = $normalized.Substring('HKCU\'.Length)
        }
    }

    if ($normalized.StartsWith('HKEY_LOCAL_MACHINE\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            Hive = 'HKLM'
            RootName = 'HKEY_LOCAL_MACHINE'
            SubPath = $normalized.Substring('HKEY_LOCAL_MACHINE\'.Length)
        }
    }

    if ($normalized.StartsWith('HKLM\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            Hive = 'HKLM'
            RootName = 'HKEY_LOCAL_MACHINE'
            SubPath = $normalized.Substring('HKLM\'.Length)
        }
    }

    return $null
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

function Get-TargetFromRegPath {
    param([Parameter(Mandatory = $true)][string]$RegPath)

    $parts = Convert-RegPathToParts -RegPath $RegPath
    if (-not $parts) {
        return $null
    }

    if ($parts.SubPath.StartsWith('Software\Classes\Local Settings\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $parts.SubPath.StartsWith('Software\Wow6432Node\Classes\Local Settings\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $segments = $parts.SubPath -split '\\'
    for ($i = 0; $i -lt ($segments.Count - 2); $i++) {
        if ($segments[$i].Equals('shellex', [System.StringComparison]::OrdinalIgnoreCase) -and
            $segments[$i + 1].Equals('ContextMenuHandlers', [System.StringComparison]::OrdinalIgnoreCase)) {
            $targetSubPath = ($segments[0..($i + 2)] -join '\')
            return [pscustomobject]@{
                Hive = $parts.Hive
                RootName = $parts.RootName
                Mode = 'ShellexHandler'
                TargetSubPath = $targetSubPath
                TargetRegPath = "$($parts.RootName)\$targetSubPath"
                TargetPsPath = "Registry::$($parts.RootName)\$targetSubPath"
            }
        }
    }

    for ($i = 0; $i -lt ($segments.Count - 1); $i++) {
        if ($segments[$i].Equals('shell', [System.StringComparison]::OrdinalIgnoreCase)) {
            $targetSubPath = ($segments[0..($i + 1)] -join '\')
            return [pscustomobject]@{
                Hive = $parts.Hive
                RootName = $parts.RootName
                Mode = 'ShellVerb'
                TargetSubPath = $targetSubPath
                TargetRegPath = "$($parts.RootName)\$targetSubPath"
                TargetPsPath = "Registry::$($parts.RootName)\$targetSubPath"
            }
        }
    }

    return $null
}

function Get-SafeFileName {
    param([Parameter(Mandatory = $true)][string]$Value)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = [BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))).Replace('-', '').Substring(0, 12)
    }
    finally {
        $sha.Dispose()
    }

    $leaf = ($Value -replace '[\\/:*?"<>| ]+', '_').Trim('_')
    if ($leaf.Length -gt 90) {
        $leaf = $leaf.Substring($leaf.Length - 90)
    }

    return "$leaf-$hash.reg"
}

function Export-RegistryKey {
    param(
        [Parameter(Mandatory = $true)][string]$RegPath,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if ($DryRun) {
        return [pscustomobject]@{ Success = $true; Output = 'Dry run; export skipped.' }
    }

    $output = & reg.exe export $RegPath $Destination /y 2>&1
    return [pscustomobject]@{
        Success = ($LASTEXITCODE -eq 0)
        Output = ($output -join "`n")
    }
}

function Get-ValueState {
    param(
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryKey]$Key,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $exists = $false
    foreach ($valueName in $Key.GetValueNames()) {
        if ($valueName.Equals($Name, [System.StringComparison]::OrdinalIgnoreCase)) {
            $exists = $true
            break
        }
    }

    if (-not $exists) {
        return [pscustomobject]@{ Exists = $false; Value = $null; Kind = $null }
    }

    return [pscustomobject]@{
        Exists = $true
        Value = $Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        Kind = $Key.GetValueKind($Name).ToString()
    }
}

function Search-PdfGearRegistryEvidence {
    $evidence = @()

    Write-SearchLine "PDF Gear registry context-menu search started: $(Get-Date -Format o)"
    foreach ($root in $searchRoots) {
        foreach ($needle in $needles) {
            Write-SearchLine ""
            Write-SearchLine "### reg query $root /f `"$needle`" /s"
            $output = & reg.exe query $root /f $needle /s 2>&1
            $exitCode = $LASTEXITCODE
            $output | ForEach-Object { Write-SearchLine $_ }

            if ($exitCode -ne 0) {
                continue
            }

            $currentKey = $null
            foreach ($line in $output) {
                $text = [string]$line
                $trimmed = $text.Trim()
                if ($trimmed -match '^(HKEY_CURRENT_USER|HKEY_LOCAL_MACHINE)\\') {
                    $currentKey = $trimmed
                    $target = Get-TargetFromRegPath -RegPath $currentKey
                    if ($target) {
                        $evidence += [pscustomobject]@{ EvidenceRegPath = $currentKey; Needle = $needle; Target = $target }
                    }
                    continue
                }

                if ($currentKey -and $trimmed.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $target = Get-TargetFromRegPath -RegPath $currentKey
                    if ($target) {
                        $evidence += [pscustomobject]@{ EvidenceRegPath = $currentKey; Needle = $needle; Target = $target }
                    }
                }
            }
        }
    }

    return $evidence
}

function Disable-ShellVerb {
    param(
        [Parameter(Mandatory = $true)]$Target,
        [Parameter(Mandatory = $true)][string]$BackupFile,
        [Parameter(Mandatory = $true)][bool]$Exported
    )

    $root = Get-RegistryRoot -Hive $Target.Hive
    $key = $root.OpenSubKey($Target.TargetSubPath, -not $DryRun)
    if (-not $key) {
        throw "Could not open shell verb key writable: $($Target.TargetRegPath)"
    }

    try {
        $legacy = Get-ValueState -Key $key -Name 'LegacyDisable'
        $programmatic = Get-ValueState -Key $key -Name 'ProgrammaticAccessOnly'
        $marker = Get-ValueState -Key $key -Name 'PdfRightClickSuiteDisabledContextMenu'

        if (-not $DryRun) {
            $key.SetValue('LegacyDisable', '', [Microsoft.Win32.RegistryValueKind]::String)
            $key.SetValue('ProgrammaticAccessOnly', '', [Microsoft.Win32.RegistryValueKind]::String)
            $key.SetValue('PdfRightClickSuiteDisabledContextMenu', "Disabled PDF Gear context-menu verb by PdfRightClickSuite on $(Get-Date -Format o)", [Microsoft.Win32.RegistryValueKind]::String)
        }

        return [pscustomobject]@{
            Mode = 'ShellVerb'
            TargetRegPath = $Target.TargetRegPath
            TargetPsPath = $Target.TargetPsPath
            BackupFile = $BackupFile
            BackupExported = $Exported
            Success = $true
            DryRun = [bool]$DryRun
            PreviousLegacyDisable = $legacy
            PreviousProgrammaticAccessOnly = $programmatic
            PreviousMarker = $marker
            Message = 'Set LegacyDisable and ProgrammaticAccessOnly.'
        }
    }
    finally {
        $key.Close()
    }
}

function Disable-ShellexHandler {
    param(
        [Parameter(Mandatory = $true)]$Target,
        [Parameter(Mandatory = $true)][string]$BackupFile,
        [Parameter(Mandatory = $true)][bool]$Exported
    )

    $leaf = Split-Path -Leaf $Target.TargetSubPath
    $parent = Split-Path -Parent $Target.TargetSubPath
    $hashName = (Get-SafeFileName -Value $Target.TargetRegPath).Replace('.reg', '').Split('-')[-1]
    $disabledLeaf = "__PdfRightClickSuiteDisabled_PDFGear_$leaf`_$hashName"
    $disabledSubPath = if ($parent) { Join-Path $parent $disabledLeaf } else { $disabledLeaf }
    $disabledSubPath = $disabledSubPath -replace '/', '\'
    $disabledRegPath = "$($Target.RootName)\$disabledSubPath"
    $disabledPsPath = "Registry::$disabledRegPath"

    if (-not $DryRun) {
        if (Test-Path -LiteralPath $disabledPsPath) {
            throw "Disabled handler target already exists: $disabledRegPath"
        }

        Rename-Item -LiteralPath $Target.TargetPsPath -NewName $disabledLeaf -ErrorAction Stop
    }

    return [pscustomobject]@{
        Mode = 'ShellexHandler'
        TargetRegPath = $Target.TargetRegPath
        TargetPsPath = $Target.TargetPsPath
        DisabledRegPath = $disabledRegPath
        DisabledPsPath = $disabledPsPath
        BackupFile = $BackupFile
        BackupExported = $Exported
        Success = $true
        DryRun = [bool]$DryRun
        Message = 'Renamed shellex ContextMenuHandlers key.'
    }
}

$evidence = Search-PdfGearRegistryEvidence
$targetsByPath = @{}
foreach ($item in $evidence) {
    $target = $item.Target
    if (-not $target) {
        continue
    }

    $key = $target.TargetRegPath.ToUpperInvariant()
    if (-not $targetsByPath.ContainsKey($key)) {
        $targetsByPath[$key] = [pscustomobject]@{
            Target = $target
            Evidence = @()
        }
    }

    $targetsByPath[$key].Evidence += [pscustomobject]@{
        EvidenceRegPath = $item.EvidenceRegPath
        Needle = $item.Needle
    }
}

$actions = @()
foreach ($entry in $targetsByPath.Values) {
    $target = $entry.Target
    $backupFile = Join-Path $BackupRoot (Get-SafeFileName -Value $target.TargetRegPath)
    $export = Export-RegistryKey -RegPath $target.TargetRegPath -Destination $backupFile

    if (-not $export.Success) {
        $actions += [pscustomobject]@{
            Mode = $target.Mode
            TargetRegPath = $target.TargetRegPath
            TargetPsPath = $target.TargetPsPath
            BackupFile = $backupFile
            BackupExported = $false
            Success = $false
            DryRun = [bool]$DryRun
            Evidence = $entry.Evidence
            Message = "Registry export failed before modification: $($export.Output)"
        }
        continue
    }

    try {
        if ($target.Mode -eq 'ShellVerb') {
            $action = Disable-ShellVerb -Target $target -BackupFile $backupFile -Exported $true
        }
        elseif ($target.Mode -eq 'ShellexHandler') {
            $action = Disable-ShellexHandler -Target $target -BackupFile $backupFile -Exported $true
        }
        else {
            throw "Unsupported target mode: $($target.Mode)"
        }

        $action | Add-Member -MemberType NoteProperty -Name Evidence -Value $entry.Evidence
        $actions += $action
    }
    catch {
        $actions += [pscustomobject]@{
            Mode = $target.Mode
            TargetRegPath = $target.TargetRegPath
            TargetPsPath = $target.TargetPsPath
            BackupFile = $backupFile
            BackupExported = $true
            Success = $false
            DryRun = [bool]$DryRun
            Evidence = $entry.Evidence
            Message = $_.Exception.Message
        }
    }
}

if (-not $NoShellRefresh -and -not $DryRun -and ($actions | Where-Object { $_.Success }).Count -gt 0) {
    Invoke-ShellAssociationChanged
}

$successCount = @($actions | Where-Object { $_.Success }).Count
$failureCount = @($actions | Where-Object { -not $_.Success }).Count

$manifest = [pscustomobject]@{
    CreatedAt = (Get-Date -Format o)
    BackupRoot = $BackupRoot
    SearchTextPath = $searchTextPath
    DryRun = [bool]$DryRun
    CandidateCount = $targetsByPath.Count
    SuccessCount = $successCount
    FailureCount = $failureCount
    Actions = $actions
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "PDF Gear context-menu search results: $searchTextPath"
Write-Host "PDF Gear disable manifest: $manifestPath"
Write-Host "Targets found: $($manifest.CandidateCount); disabled: $($manifest.SuccessCount); failed: $($manifest.FailureCount)"

if ($manifest.FailureCount -gt 0) {
    Write-Warning 'One or more PDF Gear context-menu targets could not be disabled. See the manifest for details.'
}

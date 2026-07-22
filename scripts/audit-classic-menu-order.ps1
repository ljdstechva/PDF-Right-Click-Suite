[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\classic-top-menu\logs')
)

$ErrorActionPreference = 'Stop'

$TopClsid = '{065E1050-7F50-4FDF-94C6-19B998E64A83}'
$FallbackClsid = '{68A2F5F6-2E91-4C66-B126-896B8C6C6834}'
$TopShellKey = 'HKCU:\Software\Classes\*\shell\PdfRightClickSuite'
$FallbackHandlerKey = 'HKCU:\Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite'
$PossibleDuplicateKeys = @(
    $TopShellKey,
    'HKCU:\Software\Classes\AllFilesystemObjects\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\image\shell\PdfRightClickSuite',
    'HKCU:\Software\Classes\SystemFileAssociations\text\shell\PdfRightClickSuite',
    $FallbackHandlerKey,
    'HKCU:\Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\PdfRightClickSuite'
)

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Get-KeyValues {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $item = Get-Item -LiteralPath $Path
    $names = $item.GetValueNames()
    $values = [ordered]@{}
    foreach ($name in $names) {
        $displayName = if ($name -eq '') { '(default)' } else { $name }
        $values[$displayName] = $item.GetValue($name)
    }

    return $values
}

function Test-PdfGearDisabled {
    $representativeKeys = @(
        'HKCU:\Software\Classes\SystemFileAssociations\.pdf\shell\PDFGearTools',
        'HKCU:\Software\Classes\SystemFileAssociations\image\shell\PDFGearTools',
        'HKCU:\Software\Classes\PdfGear.App.1\shell\open'
    )

    foreach ($key in $representativeKeys) {
        $exists = Test-Path -LiteralPath $key
        $props = if ($exists) { Get-ItemProperty -LiteralPath $key } else { $null }
        [ordered]@{
            Key = $key
            Exists = $exists
            LegacyDisable = $exists -and ($null -ne $props.LegacyDisable)
            ProgrammaticAccessOnly = $exists -and ($null -ne $props.ProgrammaticAccessOnly)
            Marker = $exists -and ($null -ne $props.PdfRightClickSuiteDisabledContextMenu)
        }
    }
}

$topValues = Get-KeyValues -Path $TopShellKey
$iconValue = if ($topValues) { $topValues['Icon'] } else { $null }
$iconPath = if ($iconValue) { ($iconValue -replace ',\d+$', '') } else { $null }
$registeredEntries = foreach ($key in $PossibleDuplicateKeys) {
    if (Test-Path -LiteralPath $key) {
        [ordered]@{
            Key = $key
            Values = Get-KeyValues -Path $key
            IsDefaultTopKey = [System.String]::Equals($key, $TopShellKey, [System.StringComparison]::OrdinalIgnoreCase)
        }
    }
}

$appxPackages = @(Get-AppxPackage -Name '*PdfRightClickSuite*' -ErrorAction SilentlyContinue | Select-Object Name, PackageFullName, Version)
$result = [ordered]@{
    CreatedAt = (Get-Date).ToString('o')
    TopShellKey = $TopShellKey
    TopShellKeyExists = Test-Path -LiteralPath $TopShellKey
    TopShellKeyValues = $topValues
    TopHandlerType = if ($topValues -and $topValues['ExplorerCommandHandler'] -eq $TopClsid) { 'shell verb + ExplorerCommandHandler' } elseif (Test-Path -LiteralPath $FallbackHandlerKey) { 'IContextMenu fallback' } else { 'not registered' }
    PositionTopPresent = $topValues -and $topValues['Position'] -eq 'Top'
    MUIVerbPdfPresent = $topValues -and $topValues['MUIVerb'] -eq 'PDF'
    ExplorerCommandHandlerMatches = $topValues -and $topValues['ExplorerCommandHandler'] -eq $TopClsid
    PdfMenuIconRegistryValue = $iconValue
    PdfMenuIconPath = $iconPath
    PdfMenuIconExists = $iconPath -and (Test-Path -LiteralPath $iconPath)
    FallbackHandlerExists = Test-Path -LiteralPath $FallbackHandlerKey
    PdfGearRepresentativeStatus = @(Test-PdfGearDisabled)
    ModernMenuIntentionallyDisabled = $true
    AppxPackages = $appxPackages
}
$result.DuplicatePdfRightClickSuiteHandlerCount = @($registeredEntries | Where-Object { -not $_.IsDefaultTopKey }).Count
$result.DuplicatePdfRightClickSuiteHandlers = @($registeredEntries | Where-Object { -not $_.IsDefaultTopKey })
$result.RegisteredPdfRightClickSuiteEntries = @($registeredEntries)

$output = Join-Path $OutputDir ("classic-menu-order-audit-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding UTF8

Write-Host "Classic top menu audit: $output"
Write-Host "Handler type: $($result.TopHandlerType)"
Write-Host "Position=Top present: $($result.PositionTopPresent)"
Write-Host "PDF menu icon exists: $($result.PdfMenuIconExists)"
Write-Host "PdfRightClickSuite registry entries found: $($result.DuplicatePdfRightClickSuiteHandlerCount)"

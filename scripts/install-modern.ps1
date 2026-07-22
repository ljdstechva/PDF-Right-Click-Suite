[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$PackagePath,
    [string]$ManifestPath,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$registerScript = Join-Path $PSScriptRoot 'register-modern-menu.ps1'
if (-not (Test-Path -LiteralPath $registerScript)) {
    throw "register-modern-menu.ps1 was not found beside install-modern.ps1: $registerScript"
}

$argsList = @()
if ($InstallDir) { $argsList += @('-InstallDir', $InstallDir) }
if ($PackagePath) { $argsList += @('-PackagePath', $PackagePath) }
if ($WhatIfOnly) { $argsList += '-WhatIfOnly' }

& $registerScript @argsList

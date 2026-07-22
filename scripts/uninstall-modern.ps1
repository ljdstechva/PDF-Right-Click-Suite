[CmdletBinding()]
param(
    [switch]$WhatIfOnly,
    [switch]$KeepCertificate
)

$ErrorActionPreference = 'Stop'
$unregisterScript = Join-Path $PSScriptRoot 'unregister-modern-menu.ps1'
if (-not (Test-Path -LiteralPath $unregisterScript)) {
    throw "unregister-modern-menu.ps1 was not found beside uninstall-modern.ps1: $unregisterScript"
}

$argsList = @()
if ($WhatIfOnly) { $argsList += '-WhatIfOnly' }
if ($KeepCertificate) { $argsList += '-KeepCertificate' }

& $unregisterScript @argsList

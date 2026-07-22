[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$PackagePath,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$LogDir = Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\logs'
$ModernLog = Join-Path $LogDir 'modern-menu.log'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Write-ModernLog {
    param([string]$Message)
    Add-Content -LiteralPath $ModernLog -Value "$(Get-Date -Format o) $Message"
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '\"') + '"'
}

$registerScript = Join-Path $PSScriptRoot 'register-modern-menu.ps1'
if (-not (Test-Path -LiteralPath $registerScript)) {
    throw "register-modern-menu.ps1 was not found beside register-modern-menu-elevated.ps1: $registerScript"
}

$argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Quote-Argument $registerScript))
if ($InstallDir) {
    $argsList += @('-InstallDir', (Quote-Argument $InstallDir))
}
if ($PackagePath) {
    $argsList += @('-PackagePath', (Quote-Argument $PackagePath))
}
if ($WhatIfOnly) {
    $argsList += '-WhatIfOnly'
}

if ($WhatIfOnly -or (Test-IsAdministrator)) {
    Write-ModernLog "Running modern registration directly elevated=$((Test-IsAdministrator)) whatIf=$WhatIfOnly"
    & $registerScript @PSBoundParameters
    exit $LASTEXITCODE
}

Write-ModernLog 'Launching elevated modern registration for LocalMachine\TrustedPeople certificate trust.'
$process = Start-Process -FilePath 'powershell.exe' -ArgumentList $argsList -Verb RunAs -Wait -PassThru
Write-ModernLog "Elevated modern registration process exited code=$($process.ExitCode)"

$testScript = Join-Path $PSScriptRoot 'test-modern-menu.ps1'
if (Test-Path -LiteralPath $testScript) {
    & $testScript
    exit $LASTEXITCODE
}

exit $process.ExitCode

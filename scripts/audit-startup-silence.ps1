[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\startup-silence\logs'),
    [string]$BackupRoot = (Join-Path $env:LOCALAPPDATA 'PdfRightClickSuite\registry-backups\startup-silence'),
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null

function ConvertTo-PlainText {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) {
        return ''
    }

    return [string]$Value
}

function Test-StartupCommandIsPdfRightClickSuiteNoise {
    param([AllowNull()][string]$Command)
    if ([string]::IsNullOrWhiteSpace($Command)) {
        return $false
    }

    $text = $Command.ToLowerInvariant()
    $mentionsSuite = $text.Contains('pdfrightclicksuite') -or $text.Contains('pdf right click suite')
    $opensLogOrReport = $text -match '\\.log(\s|$|"|'''')' -or
        $text.Contains('\logs\') -or
        $text.Contains('\artifacts\diagnostics\') -or
        $text.Contains('diagnostics-') -or
        $text.Contains('self-test-')
    $runsInteractiveCli = $text.Contains('pdfrightclicksuite.cli.exe') -and
        -not $text.Contains('--request') -and
        -not $text.Contains('--install-user') -and
        -not $text.Contains('--uninstall-user')
    $runsExplicitReportCommand = $text.Contains('--diagnose') -or $text.Contains('--self-test') -or $text.Contains('--welcome')

    return ($mentionsSuite -and ($opensLogOrReport -or $runsInteractiveCli -or $runsExplicitReportCommand))
}

function Backup-RegistryKey {
    param(
        [Parameter(Mandatory = $true)][string]$RegistryPath,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $safeName = ($Name -replace '[^A-Za-z0-9_.-]', '_')
    $backupPath = Join-Path $BackupRoot "$safeName.reg"
    $regPath = $RegistryPath -replace '^HKCU:', 'HKEY_CURRENT_USER' -replace '^HKLM:', 'HKEY_LOCAL_MACHINE'
    & reg.exe export $regPath $backupPath /y | Out-Null
    return $backupPath
}

function Get-RunEntries {
    $paths = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
    )

    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-ItemProperty -LiteralPath $path
        foreach ($property in $item.PSObject.Properties) {
            if ($property.Name -like 'PS*') {
                continue
            }

            $command = ConvertTo-PlainText $property.Value
            $matched = Test-StartupCommandIsPdfRightClickSuiteNoise $command
            [pscustomobject]@{
                Kind = 'RunKey'
                Location = $path
                Name = $property.Name
                Command = $command
                Matched = $matched
                PotentialNoise = $matched
                Action = 'None'
                Backup = $null
            }
        }
    }
}

function Get-StartupFolderEntries {
    $folders = @(
        [Environment]::GetFolderPath('Startup'),
        [Environment]::GetFolderPath('CommonStartup')
    )

    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $folder)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $folder -Force) {
            $command = $file.FullName
            $matched = Test-StartupCommandIsPdfRightClickSuiteNoise $command
            [pscustomobject]@{
                Kind = 'StartupFolder'
                Location = $folder
                Name = $file.Name
                Command = $command
                Matched = $matched
                PotentialNoise = $matched
                Action = 'None'
                Backup = $null
            }
        }
    }
}

function Get-StartupApprovedEntries {
    $paths = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run',
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'
    )

    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-ItemProperty -LiteralPath $path
        foreach ($property in $item.PSObject.Properties) {
            if ($property.Name -like 'PS*') {
                continue
            }

            $name = ConvertTo-PlainText $property.Name
            [pscustomobject]@{
                Kind = 'StartupApproved'
                Location = $path
                Name = $name
                Command = ''
                Matched = $false
                PotentialNoise = Test-StartupCommandIsPdfRightClickSuiteNoise $name
                Action = 'InventoryOnly'
                Backup = $null
            }
        }
    }
}

function Resolve-ShortcutTarget {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($Path)
        return (@($shortcut.TargetPath, $shortcut.Arguments, $shortcut.WorkingDirectory) -join ' ').Trim()
    }
    catch {
        return $Path
    }
}

function Get-StartMenuShortcutEntries {
    $folders = @(
        [Environment]::GetFolderPath('Programs'),
        [Environment]::GetFolderPath('CommonPrograms')
    )

    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $folder)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -Filter '*.lnk' -Force -ErrorAction SilentlyContinue) {
            $command = Resolve-ShortcutTarget $file.FullName
            [pscustomobject]@{
                Kind = 'StartMenuShortcut'
                Location = $folder
                Name = $file.FullName
                Command = $command
                Matched = $false
                PotentialNoise = Test-StartupCommandIsPdfRightClickSuiteNoise $command
                Action = 'InventoryOnly'
                Backup = $null
            }
        }
    }
}

function Get-ScheduledTaskEntries {
    foreach ($task in Get-ScheduledTask) {
        foreach ($action in $task.Actions) {
            $command = @($action.Execute, $action.Arguments, $action.WorkingDirectory) -join ' '
            $matched = Test-StartupCommandIsPdfRightClickSuiteNoise $command
            [pscustomobject]@{
                Kind = 'ScheduledTask'
                Location = $task.TaskPath
                Name = $task.TaskName
                Command = $command.Trim()
                Matched = $matched
                PotentialNoise = $matched
                Action = 'None'
                Backup = $null
            }
        }
    }
}

$entries = @()
$entries += Get-RunEntries
$entries += Get-StartupFolderEntries
$entries += Get-StartupApprovedEntries
$entries += Get-StartMenuShortcutEntries
$entries += Get-ScheduledTaskEntries

if ($Apply) {
    foreach ($entry in $entries | Where-Object { $_.Matched }) {
        if ($entry.Kind -eq 'RunKey') {
            $entry.Backup = Backup-RegistryKey -RegistryPath $entry.Location -Name "$($entry.Kind)-$($entry.Name)"
            Remove-ItemProperty -LiteralPath $entry.Location -Name $entry.Name -ErrorAction Stop
            $entry.Action = 'RemovedRunValue'
        }
        elseif ($entry.Kind -eq 'StartupFolder') {
            $backup = Join-Path $BackupRoot $entry.Name
            Copy-Item -LiteralPath $entry.Command -Destination $backup -Force
            Remove-Item -LiteralPath $entry.Command -Force
            $entry.Backup = $backup
            $entry.Action = 'RemovedStartupShortcut'
        }
        elseif ($entry.Kind -eq 'ScheduledTask') {
            $backup = Join-Path $BackupRoot ("ScheduledTask-{0}.xml" -f ($entry.Name -replace '[^A-Za-z0-9_.-]', '_'))
            Export-ScheduledTask -TaskName $entry.Name -TaskPath $entry.Location | Set-Content -LiteralPath $backup -Encoding UTF8
            Disable-ScheduledTask -TaskName $entry.Name -TaskPath $entry.Location | Out-Null
            $entry.Backup = $backup
            $entry.Action = 'DisabledScheduledTask'
        }
    }
}

$receipt = [ordered]@{
    Timestamp = Get-Date -Format o
    Apply = [bool]$Apply
    MatchedCount = @($entries | Where-Object { $_.Matched }).Count
    Entries = @($entries)
}

$path = Join-Path $OutputDir ("startup-silence-audit-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
Write-Host "Startup silence audit: $path"
Write-Host "Matched noisy startup entries: $($receipt.MatchedCount)"

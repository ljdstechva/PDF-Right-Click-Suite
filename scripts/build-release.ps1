[CmdletBinding()]
param(
    [switch]$SkipNative
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = Join-Path $RepoRoot 'artifacts\release'
$AppOut = Join-Path $ArtifactsRoot 'app'
$ScriptsOut = Join-Path $ArtifactsRoot 'scripts'
$OptionalModernOut = Join-Path $ArtifactsRoot 'optional-modern-menu'

function Get-DotNet {
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $userDotnet) {
        $sdks = & $userDotnet --list-sdks
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            return $userDotnet
        }
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $sdks = & $dotnet.Source --list-sdks
        if ($LASTEXITCODE -eq 0 -and $sdks) {
            return $dotnet.Source
        }
    }

    throw 'dotnet SDK was not found. Install .NET 8 SDK or run dotnet-install into %USERPROFILE%\.dotnet.'
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-MSBuild {
    $msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuild) {
        return $msbuild.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path) {
            return $path
        }
    }

    return $null
}

function Get-PrebuiltNativeDll {
    $candidates = @(
        (Join-Path $RepoRoot 'native\PdfRightClickSuite.ShellExtension\x64\Release\PdfRightClickSuite.ShellExtension.dll'),
        (Join-Path $env:LOCALAPPDATA 'Programs\PdfRightClickSuite\PdfRightClickSuite.ShellExtension.dll')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Clear-ReleaseDirectory {
    $fullArtifacts = [System.IO.Path]::GetFullPath($ArtifactsRoot)
    $fullRepo = [System.IO.Path]::GetFullPath($RepoRoot)
    if (-not $fullArtifacts.StartsWith($fullRepo, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear artifacts outside repo: $fullArtifacts"
    }

    if (Test-Path -LiteralPath $ArtifactsRoot) {
        Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $AppOut | Out-Null
    New-Item -ItemType Directory -Force -Path $ScriptsOut | Out-Null
    New-Item -ItemType Directory -Force -Path $OptionalModernOut | Out-Null
}

Push-Location $RepoRoot
try {
    $dotnet = Get-DotNet
    Clear-ReleaseDirectory

    Invoke-Checked -FilePath $dotnet -Arguments @('restore', (Join-Path $RepoRoot 'PdfRightClickSuite.sln'))
    Invoke-Checked -FilePath $dotnet -Arguments @('test', (Join-Path $RepoRoot 'PdfRightClickSuite.sln'), '--configuration', 'Release', '--no-restore')
    Invoke-Checked -FilePath $dotnet -Arguments @(
        'publish',
        (Join-Path $RepoRoot 'src\PdfRightClickSuite.Cli\PdfRightClickSuite.Cli.csproj'),
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '--output',
        $AppOut)

    if (-not $SkipNative) {
        $msbuild = Get-MSBuild
        if ($msbuild) {
            $nativeProject = Join-Path $RepoRoot 'native\PdfRightClickSuite.ShellExtension\PdfRightClickSuite.ShellExtension.vcxproj'
            Invoke-Checked -FilePath $msbuild -Arguments @($nativeProject, '/m', '/p:Configuration=Release', '/p:Platform=x64')
            $nativeDll = Join-Path $RepoRoot 'native\PdfRightClickSuite.ShellExtension\x64\Release\PdfRightClickSuite.ShellExtension.dll'
            if (-not (Test-Path -LiteralPath $nativeDll)) {
                throw "Native shell extension build completed but DLL was not found: $nativeDll"
            }

            Copy-Item -LiteralPath $nativeDll -Destination $AppOut -Force
        }
        else {
            $prebuiltNative = Get-PrebuiltNativeDll
            if ($prebuiltNative) {
                Copy-Item -LiteralPath $prebuiltNative -Destination $AppOut -Force
                @"
Native shell extension was not rebuilt because MSBuild with Visual C++ Build Tools was not found.

The release used this existing prebuilt native DLL:
  $prebuiltNative

Install "Visual Studio 2022 Build Tools" with the "Desktop development with C++" workload,
then rerun:
  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
"@ | Set-Content -LiteralPath (Join-Path $ArtifactsRoot 'NativeBuildFallback.txt')
                Write-Warning "Native shell extension was not rebuilt; copied existing DLL from $prebuiltNative."
            }
            else {
            @'
Native shell extension was not built because MSBuild with Visual C++ Build Tools was not found.

Install "Visual Studio 2022 Build Tools" with the "Desktop development with C++" workload,
then rerun:
  powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
'@ | Set-Content -LiteralPath (Join-Path $ArtifactsRoot 'NativeBuildRequired.txt')
            Write-Warning 'Native shell extension was not built; see artifacts\release\NativeBuildRequired.txt.'
            }
        }
    }

    $pdfIconSource = Join-Path $RepoRoot 'assets\icons\pdf.ico'
    if (-not (Test-Path -LiteralPath $pdfIconSource)) {
        throw "PDF menu icon asset was not found: $pdfIconSource"
    }

    $appAssetsOut = Join-Path $AppOut 'assets'
    New-Item -ItemType Directory -Force -Path $appAssetsOut | Out-Null
    Copy-Item -LiteralPath $pdfIconSource -Destination (Join-Path $appAssetsOut 'pdf.ico') -Force

    $thirdPartySummary = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES.md'
    $thirdPartySource = Join-Path $RepoRoot 'third-party'
    if (-not (Test-Path -LiteralPath $thirdPartySummary) -or -not (Test-Path -LiteralPath $thirdPartySource)) {
        throw 'Third-party license notices were not found in the repository.'
    }

    Copy-Item -LiteralPath $thirdPartySummary -Destination $AppOut -Force
    $thirdPartyOut = Join-Path $AppOut 'third-party'
    New-Item -ItemType Directory -Force -Path $thirdPartyOut | Out-Null
    Get-ChildItem -LiteralPath $thirdPartySource -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $thirdPartyOut -Force
    }

    $appScriptsOut = Join-Path $AppOut 'scripts'
    New-Item -ItemType Directory -Force -Path $appScriptsOut | Out-Null
    foreach ($scriptName in @('install.ps1', 'uninstall.ps1', 'install-classic-top-menu.ps1', 'uninstall-classic-top-menu.ps1', 'audit-classic-menu-order.ps1', 'audit-startup-silence.ps1', 'disable-pdfgear-context-menu.ps1', 'restore-pdfgear-context-menu.ps1')) {
        $scriptPath = Join-Path $RepoRoot "scripts\$scriptName"
        if (Test-Path -LiteralPath $scriptPath) {
            Copy-Item -LiteralPath $scriptPath -Destination $ScriptsOut -Force
            Copy-Item -LiteralPath $scriptPath -Destination $appScriptsOut -Force
        }
    }

    $buildReleaseScript = Join-Path $RepoRoot 'scripts\build-release.ps1'
    if (Test-Path -LiteralPath $buildReleaseScript) {
        Copy-Item -LiteralPath $buildReleaseScript -Destination $ScriptsOut -Force
    }

    $optionalModernScriptsOut = Join-Path $OptionalModernOut 'scripts'
    New-Item -ItemType Directory -Force -Path $optionalModernScriptsOut | Out-Null
    foreach ($scriptName in @('install-modern.ps1', 'uninstall-modern.ps1', 'register-modern-menu.ps1', 'register-modern-menu-elevated.ps1', 'unregister-modern-menu.ps1', 'test-modern-menu.ps1')) {
        $scriptPath = Join-Path $RepoRoot "scripts\$scriptName"
        if (Test-Path -LiteralPath $scriptPath) {
            Copy-Item -LiteralPath $scriptPath -Destination $optionalModernScriptsOut -Force
        }
    }

    $modernSource = Join-Path $RepoRoot 'modern'
    if (Test-Path -LiteralPath $modernSource) {
        $modernOut = Join-Path $OptionalModernOut 'modern'
        Copy-Item -LiteralPath $modernSource -Destination $modernOut -Recurse -Force

        $sparsePackage = Join-Path $RepoRoot 'artifacts\modern\PdfRightClickSuite.sparse.msix'
        if (Test-Path -LiteralPath $sparsePackage) {
            Copy-Item -LiteralPath $sparsePackage -Destination $modernOut -Force
        }
    }

    @"
PdfRightClickSuite release output

App folder:
  $AppOut

Install scripts:
  $ScriptsOut

Optional modern-menu archive:
  $OptionalModernOut

CLI:
  $(Join-Path $AppOut 'PdfRightClickSuite.Cli.exe')

Shell extension:
  $(Join-Path $AppOut 'PdfRightClickSuite.ShellExtension.dll')

PDF menu icon:
  $(Join-Path $AppOut 'assets\pdf.ico')

Third-party notices:
  $(Join-Path $AppOut 'THIRD-PARTY-NOTICES.md')
"@ | Set-Content -LiteralPath (Join-Path $ArtifactsRoot 'README_RELEASE.txt')

    Write-Host "Release artifacts written to $ArtifactsRoot"
}
finally {
    Pop-Location
}

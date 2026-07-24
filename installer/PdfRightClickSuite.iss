; Build after running scripts\build-release.ps1:
;   ISCC.exe installer\PdfRightClickSuite.iss

#define AppName "PdfRightClickSuite"
#define AppVersion "1.1.0"
#define ReleaseApp "..\artifacts\release\app"

[Setup]
AppId={{68A2F5F6-2E91-4C66-B126-896B8C6C6834}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PdfRightClickSuite
DefaultDirName={localappdata}\Programs\PdfRightClickSuite
DefaultGroupName=PdfRightClickSuite
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=PdfRightClickSuiteSetup
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\PdfRightClickSuite.Cli.exe

[Files]
Source: "{#ReleaseApp}\*"; DestDir: "{app}"; Excludes: "PdfRightClickSuite.ShellExtension.dll,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseApp}\PdfRightClickSuite.ShellExtension.dll"; DestDir: "{app}"; Flags: ignoreversion regserver

[InstallDelete]
Type: files; Name: "{app}\*.pdb"

[Icons]
Name: "{group}\PdfRightClickSuite Diagnostics"; Filename: "{app}\PdfRightClickSuite.Cli.exe"; Parameters: "--diagnose"
Name: "{group}\PdfRightClickSuite Self-Test"; Filename: "{app}\PdfRightClickSuite.Cli.exe"; Parameters: "--self-test"
Name: "{group}\Uninstall PdfRightClickSuite"; Filename: "{uninstallexe}"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install.ps1"" -SourcePath ""{app}"" -InstallDir ""{app}"" -NoRestartPrompt"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall.ps1"" -InstallDir ""{app}"" -NoRestartPrompt"; WorkingDir: "{app}"; Flags: runhidden waituntilterminated; RunOnceId: "PdfRightClickSuiteUninstall"

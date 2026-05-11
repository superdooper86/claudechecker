#ifndef AppVersion
  #define AppVersion "0.0.1"
#endif

[Setup]
AppName=ClaudeChecker
AppVersion={#AppVersion}
AppPublisher=superdooper86
AppPublisherURL=https://github.com/superdooper86/claudechecker
AppSupportURL=https://github.com/superdooper86/claudechecker/issues
AppUpdatesURL=https://github.com/superdooper86/claudechecker/releases
DefaultDirName={localappdata}\ClaudeChecker
DefaultGroupName=ClaudeChecker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=ClaudeChecker-Installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\ClaudeChecker.exe
SourceDir=..

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\ClaudeChecker.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\ClaudeChecker"; Filename: "{app}\ClaudeChecker.exe"
Name: "{commondesktop}\ClaudeChecker"; Filename: "{app}\ClaudeChecker.exe"; Tasks: desktopicon

[Run]
; Silent install (auto-update): launch automatically
Filename: "{app}\ClaudeChecker.exe"; Flags: nowait; Check: WizardSilent
; Manual install: show launch checkbox on final page
Filename: "{app}\ClaudeChecker.exe"; Description: "{cm:LaunchProgram,ClaudeChecker}"; Flags: nowait postinstall skipifsilent

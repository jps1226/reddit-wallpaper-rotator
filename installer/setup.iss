; Inno Setup script for Reddit Wallpaper Rotator
; Build the app first (build.ps1), then compile this with Inno Setup 6 (ISCC.exe).

#define AppName "Reddit Wallpaper Rotator"
#define AppVersion "1.0.0"
#define AppPublisher "WallpaperReddit"
#define AppExeName "WallpaperReddit.exe"

[Setup]
AppId={{9F3C4E21-7B4D-4A6C-9E1F-2A5B8C7D0E11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\WallpaperReddit
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=Output
OutputBaseFilename=RedditWallpaperRotator-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
SetupIconFile=..\src\WallpaperReddit\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"
Name: "startupicon"; Description: "Start automatically when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
; The whole self-contained publish folder. For a single-file publish this is just the
; one exe (no runtime prerequisite); for a framework/multi-file publish it also carries
; the side-by-side dlls.
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--minimized"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove per-user data folder on uninstall.
Type: filesandordirs; Name: "{localappdata}\WallpaperReddit"

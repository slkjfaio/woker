#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif

[Setup]
AppId={{9E69326C-9292-4B86-A10A-63257A932E97}
AppName=WorkBench
AppVersion={#AppVersion}
AppPublisher=WorkBench
DefaultDirName={localappdata}\Programs\WorkBench
DisableDirPage=no
UsePreviousAppDir=yes
DefaultGroupName=WorkBench
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=WorkBench-{#AppVersion}-win-x64-Setup
SetupIconFile=..\woker\woker\Assets\WorkBench.ico
UninstallDisplayIcon={app}\woker.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WorkBench"; Filename: "{app}\woker.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\WorkBench"; Filename: "{app}\woker.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\woker.exe"; Description: "Launch WorkBench"; Flags: nowait postinstall skipifsilent

; User data under %LOCALAPPDATA%\WorkBench is deliberately not installed or deleted here.

#ifndef Arch
  #error Arch must be passed from the packaging workflow
#endif
#ifndef AppVersion
  #error AppVersion must be passed from the packaging workflow
#endif
#ifndef SourceDir
  #error SourceDir must be passed from the packaging workflow
#endif
[Setup]
AppId={{8498D687-CB0C-4984-A140-8432B88394D0}
AppName=Najm Card
AppVersion={#AppVersion}
AppPublisher=Najm Card
DefaultDirName={localappdata}\Programs\NajmCard
DefaultGroupName=Najm Card
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\src\NajmCard.Desktop\Assets\NajmCard.ico
UninstallDisplayIcon={app}\NajmCard.exe
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x86compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Najm Card"; Filename: "{app}\NajmCard.exe"
Name: "{autodesktop}\Najm Card"; Filename: "{app}\NajmCard.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\NajmCard.exe"; Description: "Launch Najm Card"; Flags: nowait postinstall skipifsilent

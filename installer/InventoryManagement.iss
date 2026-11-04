; Compiled by build-installer.ps1, or by hand from the Inno Setup app. Either way the app must
; already be published to .\publish, and the version comes from the published exe.
#define AppVersion RemoveFileExt(GetVersionNumbersString(AddBackslash(SourcePath) + "publish\InventoryManagement.exe"))

[Setup]
; Never change AppId: it is what lets a new version install over an old one.
AppId={{7D3F2A64-9B1E-4C58-A0D7-3E6C1F8B5A92}
AppName=Inventory Management
AppVersion={#AppVersion}
AppPublisher=Inventory Management
DefaultDirName={autopf}\Inventory Management
DefaultGroupName=Inventory Management
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\InventoryManagement.exe
OutputDir=output
OutputBaseFilename=InventoryManagement-Setup-{#AppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\InventoryManagement.App\Assets\app.ico
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp

[Files]
; Customer data lives in %AppData%\InventoryManagement (see AppPaths), which
; this installer never touches: upgrading or uninstalling keeps it.
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Inventory Management"; Filename: "{app}\InventoryManagement.exe"
Name: "{autodesktop}\Inventory Management"; Filename: "{app}\InventoryManagement.exe"

[Run]
Filename: "{app}\InventoryManagement.exe"; Description: "Open Inventory Management"; Flags: postinstall nowait skipifsilent

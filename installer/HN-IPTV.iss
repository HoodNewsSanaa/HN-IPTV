#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif
#ifndef PlaybackEngine
  #define PlaybackEngine "vlc"
#endif
[Setup]
AppId={{CD93EEDF-5518-4324-92BD-06600458F64C}
AppName=HN-IPTV
AppVersion={#AppVersion}
AppVerName=HN-IPTV {#AppVersion}
AppPublisher=HN-IPTV Project
DefaultDirName={localappdata}\Programs\HN-IPTV
DefaultGroupName=HN-IPTV
; Keep this AppId, installation scope and data location stable across releases.
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=HN-IPTV-Setup
#ifdef QuickCompression
Compression=lzma2/fast
#else
Compression=lzma2/ultra64
#endif
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\HN-IPTV\Assets\HN-IPTV.ico
UninstallDisplayIcon={app}\HN-IPTV.exe
CloseApplications=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupMutex=HN-IPTV-Setup
AppMutex=HN-IPTV-Application
UninstallLogMode=append
VersionInfoDescription=HN-IPTV Windows installer

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
#if PlaybackEngine == "mpv"
Type: filesandordirs; Name: "{app}\libvlc"
Type: files; Name: "{app}\LibVLCSharp.dll"
#else
Type: files; Name: "{app}\libmpv-2.dll"
#endif

; Remove architectures shipped by Update 6 during an in-place upgrade.
Type: filesandordirs; Name: "{app}\libvlc\win-x86"
Type: filesandordirs; Name: "{app}\libvlc\win-arm64"
Type: files; Name: "{app}\LibVLCSharp.WPF.dll"

[Icons]
Name: "{autoprograms}\HN-IPTV"; Filename: "{app}\HN-IPTV.exe"
Name: "{autodesktop}\HN-IPTV"; Filename: "{app}\HN-IPTV.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\HN-IPTV.exe"; Description: "Launch HN-IPTV"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Delete HN-IPTV user data, playlists, EPG cache, logos and favorites? This cannot be undone.',
        mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\HN-IPTV'), True, True, True);
end;

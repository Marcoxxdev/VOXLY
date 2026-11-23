; Instalator klienta VOXLY (Inno Setup 6.1 lub nowszy), autor: Marcoxxdev
; Kompilowany przez build-all.bat. Klient jest "self-contained" (zawiera srodowisko .NET),
; instalator dodatkowo pobiera i instaluje Microsoft Visual C++ Runtime, jesli go brakuje.
#ifndef AppVersion
  #define AppVersion "1.6.0"
#endif
[Setup]
AppId={{B7C1F0A2-5D3E-4E8A-9C11-6A2F3D9E7B40}
AppName=VOXLY
AppVersion={#AppVersion}
AppPublisher=Marcoxxdev
DefaultDirName={autopf}\VOXLY
DefaultGroupName=VOXLY
SetupIconFile=..\Client\Assets\voxly.ico
OutputDir=..\dist
OutputBaseFilename=Voxly-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\Voxly.exe
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utworz skrot na pulpicie"; GroupDescription: "Skroty:"

[Files]
Source: "..\dist\client\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\VOXLY"; Filename: "{app}\Voxly.exe"
Name: "{autodesktop}\VOXLY"; Filename: "{app}\Voxly.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Instalowanie Microsoft Visual C++ Runtime..."; Check: VCRedistReady; Flags: shellexec waituntilterminated
Filename: "{app}\Voxly.exe"; Description: "Uruchom VOXLY"; Flags: nowait postinstall skipifsilent
; po aktualizacji z poziomu programu (/SILENT /restart=1) uruchamiamy nową wersję automatycznie
Filename: "{app}\Voxly.exe"; Flags: nowait runasoriginaluser; Check: RestartAfterUpdate

[Code]
var
  DownloadPage: TDownloadWizardPage;

function VCRedistMissing: Boolean;
var
  Installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
    Result := (Installed <> 1);
end;

function RestartAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:restart|0}') = '1';
end;

function VCRedistReady: Boolean;
begin
  Result := VCRedistMissing and FileExists(ExpandConstant('{tmp}\vc_redist.x64.exe'));
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and VCRedistMissing then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if not DownloadPage.AbortedByUser then
          SuppressibleMsgBox('Nie udalo sie pobrac Microsoft Visual C++ Runtime. Instalacja VOXLY bedzie kontynuowana. ' + 'Jesli program sie nie uruchomi, zainstaluj: https://aka.ms/vs/17/release/vc_redist.x64.exe', mbInformation, MB_OK, IDOK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

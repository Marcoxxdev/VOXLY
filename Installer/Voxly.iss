; Instalator klienta VOXLY (Inno Setup 6.1 lub nowszy), autor: Marcoxxdev
; Dwujezyczny: polski i angielski (wybor jezyka wg jezyka systemu; gdy brak dopasowania, pokazuje okno wyboru).
; Pliki .iss sa tu celowo zapisane bez polskich znakow (ANSI), dlatego polskie napisy sa bez ogonkow.
; Kompilowany przez build-all.bat. Klient jest "self-contained" (zawiera srodowisko .NET),
; instalator dodatkowo pobiera i instaluje Microsoft Visual C++ Runtime, jesli go brakuje.
#ifndef AppVersion
  #define AppVersion "1.9.7"
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
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=auto
DisableProgramGroupPage=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
polish.VCRedistInstalling=Instalowanie Microsoft Visual C++ Runtime...
english.VCRedistInstalling=Installing Microsoft Visual C++ Runtime...
polish.VCRedistDownloadFailed=Nie udalo sie pobrac Microsoft Visual C++ Runtime. Instalacja VOXLY bedzie kontynuowana. Jesli program sie nie uruchomi, zainstaluj: https://aka.ms/vs/17/release/vc_redist.x64.exe
english.VCRedistDownloadFailed=Could not download Microsoft Visual C++ Runtime. The VOXLY installation will continue. If the program does not start, install it from: https://aka.ms/vs/17/release/vc_redist.x64.exe

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\dist\client\Voxly.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\VOXLY"; Filename: "{app}\Voxly.exe"
Name: "{autodesktop}\VOXLY"; Filename: "{app}\Voxly.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "{cm:VCRedistInstalling}"; Check: VCRedistReady; Flags: shellexec waituntilterminated
Filename: "{app}\Voxly.exe"; Description: "{cm:LaunchProgram,VOXLY}"; Flags: nowait postinstall skipifsilent
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
          SuppressibleMsgBox(CustomMessage('VCRedistDownloadFailed'), mbInformation, MB_OK, IDOK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

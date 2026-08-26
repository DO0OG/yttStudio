; yttStudio Windows 설치 프로그램 (Inno Setup 6)
;
; 릴리즈 워크플로가 아래 정의를 넘긴다.
;   /DAppVersion=0.1.0  /DSourceDir=<publish 경로>  /DOutputDir=<artifacts 경로>
;   /DHaveKorean        (컴파일러에 Korean.isl 이 있을 때만)
; 로컬에서 시험할 때는 정의 없이 그대로 컴파일해도 기본값으로 동작한다.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts"
#endif

#define AppName "yttStudio"
#define AppExeName "YttStudio.App.exe"
#define AppPublisher "DO0OG"
#define AppUrl "https://github.com/DO0OG/yttStudio"

[Setup]
; 이 GUID 는 업그레이드 판정 기준이다. 절대 바꾸지 않는다.
AppId={{7B2F1C64-9E43-4A18-8C5D-2F0A6B3E9D71}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=yttStudio-v{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\YttStudio.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 관리자 권한 없이 사용자 폴더에만 설치하는 선택지를 준다.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; x64compatible 은 Inno Setup 6.3 부터다. 그 이전 컴파일러도 받아들이도록 분기한다.
#if Ver >= EncodeVer(6,3,0)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#endif
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
#ifdef HaveKorean
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#endif

[CustomMessages]
english.AssocGroup=File associations:
english.AssocSubtitle=Open .ytt, .srv3 and .yttproj files with yttStudio
english.AssocAss=Also open .ass files with yttStudio
english.TypeYtt=YouTube Timed Text subtitle
english.TypeSrv3=YouTube SRV3 subtitle
english.TypeAss=Advanced SubStation Alpha subtitle
english.TypeProj=yttStudio project
japanese.AssocGroup=ファイルの関連付け:
japanese.AssocSubtitle=.ytt / .srv3 / .yttproj を yttStudio で開く
japanese.AssocAss=.ass も yttStudio で開く
japanese.TypeYtt=YouTube Timed Text 字幕
japanese.TypeSrv3=YouTube SRV3 字幕
japanese.TypeAss=Advanced SubStation Alpha 字幕
japanese.TypeProj=yttStudio プロジェクト
#ifdef HaveKorean
korean.AssocGroup=파일 연결:
korean.AssocSubtitle=.ytt · .srv3 · .yttproj 파일을 yttStudio 로 열기
korean.AssocAss=.ass 파일도 yttStudio 로 열기
korean.TypeYtt=YouTube Timed Text 자막
korean.TypeSrv3=YouTube SRV3 자막
korean.TypeAss=Advanced SubStation Alpha 자막
korean.TypeProj=yttStudio 프로젝트
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "associate"; Description: "{cm:AssocSubtitle}"; GroupDescription: "{cm:AssocGroup}"
; .ass 는 Aegisub 같은 기존 도구가 이미 쥐고 있을 수 있어 기본 해제다.
Name: "associateass"; Description: "{cm:AssocAss}"; GroupDescription: "{cm:AssocGroup}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\yttStudio.ytt"; ValueType: string; ValueName: ""; ValueData: "{cm:TypeYtt}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\yttStudio.ytt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\yttStudio.ytt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.ytt\OpenWithProgids"; ValueType: string; ValueName: "yttStudio.ytt"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate

Root: HKA; Subkey: "Software\Classes\yttStudio.srv3"; ValueType: string; ValueName: ""; ValueData: "{cm:TypeSrv3}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\yttStudio.srv3\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\yttStudio.srv3\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.srv3\OpenWithProgids"; ValueType: string; ValueName: "yttStudio.srv3"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate

Root: HKA; Subkey: "Software\Classes\yttStudio.yttproj"; ValueType: string; ValueName: ""; ValueData: "{cm:TypeProj}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\yttStudio.yttproj\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\yttStudio.yttproj\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.yttproj\OpenWithProgids"; ValueType: string; ValueName: "yttStudio.yttproj"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate

Root: HKA; Subkey: "Software\Classes\yttStudio.ass"; ValueType: string; ValueName: ""; ValueData: "{cm:TypeAss}"; Flags: uninsdeletekey; Tasks: associateass
Root: HKA; Subkey: "Software\Classes\yttStudio.ass\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"; Tasks: associateass
Root: HKA; Subkey: "Software\Classes\yttStudio.ass\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: associateass
Root: HKA; Subkey: "Software\Classes\.ass\OpenWithProgids"; ValueType: string; ValueName: "yttStudio.ass"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associateass

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  SHCNE_ASSOCCHANGED = $08000000;
  SHCNF_IDLIST = $0000;

procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1, dwItem2: Cardinal);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // 탐색기가 방금 등록한 파일 연결을 즉시 반영하도록 알린다.
  if CurStep = ssPostInstall then
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;

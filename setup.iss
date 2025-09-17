[Setup]
AppId={{B8F9A2E5-7C3D-4F1A-9B2E-8D7C6A5F4E3D}
AppName=MurtiWifi Connector
AppVersion=2.0.0
AppVerName=MurtiWifi Connector 2.0.0
AppPublisher=MurtiSoft
AppPublisherURL=https://murtisoft.com
AppSupportURL=https://murtisoft.com/support
AppUpdatesURL=https://murtisoft.com/updates
AppCopyright=Copyright © 2025 MurtiSoft. All rights reserved.
DefaultDirName={autopf}\MurtiWifiConnecter
DefaultGroupName=MurtiWifi Connector
LicenseFile=LICENSE.txt
InfoBeforeFile=README.md
OutputDir=release
OutputBaseFilename=MurtiWifiConnector-2.0.0-Setup
SetupIconFile=icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=100
WizardResizable=yes
UninstallDisplayIcon={app}\MurtiWifiConnecter.exe
UninstallDisplayName=MurtiWifi Connector
VersionInfoVersion=2.0.0.0
VersionInfoCompany=MurtiSoft
VersionInfoDescription=個人・家庭向け WiFi 管理アプリケーション
VersionInfoCopyright=Copyright © 2025 MurtiSoft
VersionInfoProductName=MurtiWifi Connector
VersionInfoProductVersion=2.0.0
MinVersion=6.1sp1

; System requirements
; Windows 7 SP1 or later, .NET 6.0 Runtime
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Privileges
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; UI Customization
WizardImageFile=installer-sidebar.bmp
WizardSmallImageFile=installer-icon.bmp

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode
Name: "startupicon"; Description: "システム起動時に自動実行"; GroupDescription: "スタートアップ オプション"; Flags: checked
Name: "systemtray"; Description: "システムトレイに常駐"; GroupDescription: "動作オプション"; Flags: checked
Name: "associatefiles"; Description: "WiFi設定ファイル(.mwc)を関連付け"; GroupDescription: "ファイル関連付け"

[Files]
; Main application files
Source: "bin\Release\net6.0-windows\MurtiWifiConnecter.exe"; DestDir: "{app}"; Flags: ignoreversion signonce
Source: "bin\Release\net6.0-windows\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net6.0-windows\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net6.0-windows\*.config"; DestDir: "{app}"; Flags: ignoreversion

; Configuration files
Source: "default_settings.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "family_profiles.json"; DestDir: "{app}"; Flags: ignoreversion

; Documentation
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

; Batch installer helper
Source: "install.bat"; DestDir: "{app}"; Flags: ignoreversion

; Visual C++ Redistributable (if needed)
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeedsInstall

[Icons]
Name: "{group}\MurtiWifi Connector"; Filename: "{app}\MurtiWifiConnecter.exe"; IconFilename: "{app}\MurtiWifiConnecter.exe"; Comment: "個人・家庭向け WiFi 管理"
Name: "{group}\設定"; Filename: "{app}\MurtiWifiConnecter.exe"; Parameters: "--settings"; IconFilename: "{app}\MurtiWifiConnecter.exe"; Comment: "設定画面を開く"
Name: "{group}\ヘルプとサポート"; Filename: "{app}\README.md"; Comment: "ヘルプドキュメント"
Name: "{group}\{cm:UninstallProgram,MurtiWifi Connector}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MurtiWifi Connector"; Filename: "{app}\MurtiWifiConnecter.exe"; Tasks: desktopicon; Comment: "個人・家庭向け WiFi 管理"
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\MurtiWifi Connector"; Filename: "{app}\MurtiWifiConnecter.exe"; Tasks: quicklaunchicon

[Run]
; Install Visual C++ Redistributable if needed
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/quiet /norestart"; Check: VCRedistNeedsInstall; StatusMsg: "Installing Visual C++ Redistributable..."

; Configure startup options
Filename: "{app}\MurtiWifiConnecter.exe"; Parameters: "--configure-startup"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent; Tasks: startupicon; Description: "スタートアップ設定を構成"

; Launch application
Filename: "{app}\MurtiWifiConnecter.exe"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent; Description: "{cm:LaunchProgram,MurtiWifi Connector}"

[UninstallRun]
Filename: "{app}\MurtiWifiConnecter.exe"; Parameters: "--cleanup"; WorkingDir: "{app}"; Flags: skipifdoesntexist

[Registry]
; File associations
Root: HKCR; Subkey: ".mwc"; ValueType: string; ValueName: ""; ValueData: "MurtiWifiConnector.Config"; Flags: uninsdeletevalue; Tasks: associatefiles
Root: HKCR; Subkey: "MurtiWifiConnector.Config"; ValueType: string; ValueName: ""; ValueData: "MurtiWifi Connector Configuration"; Flags: uninsdeletekey; Tasks: associatefiles
Root: HKCR; Subkey: "MurtiWifiConnector.Config\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MurtiWifiConnecter.exe,0"; Tasks: associatefiles
Root: HKCR; Subkey: "MurtiWifiConnector.Config\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MurtiWifiConnecter.exe"" ""%1"""; Tasks: associatefiles

; Application registration
Root: HKLM; Subkey: "SOFTWARE\MurtiSoft\MurtiWifiConnecter"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\MurtiSoft\MurtiWifiConnecter"; ValueType: string; ValueName: "Version"; ValueData: "2.0.0"
Root: HKLM; Subkey: "SOFTWARE\MurtiSoft\MurtiWifiConnecter"; ValueType: dword; ValueName: "Installed"; ValueData: 1

; Startup entry
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MurtiWifiConnecter"; ValueData: """{app}\MurtiWifiConnecter.exe"" --startup"; Tasks: startupicon; Flags: uninsdeletevalue

[Dirs]
Name: "{app}"; Permissions: users-full
Name: "{userappdata}\MurtiWifiConnecter"; Permissions: users-full
Name: "{userappdata}\MurtiWifiConnecter\Logs"; Permissions: users-full
Name: "{userappdata}\MurtiWifiConnecter\Profiles"; Permissions: users-full
Name: "{userappdata}\MurtiWifiConnecter\Backup"; Permissions: users-full

[Code]
var
  DotNetMissing: Boolean;
  PrereqPage: TOutputMsgMemoWizardPage;

function IsDotNet6Installed(): Boolean;
var
  InstallDir: string;
begin
  Result := RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', '6.0.0', InstallDir) or
            RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', '6.0.0', InstallDir);
end;

function VCRedistNeedsInstall(): Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SOFTWARE\Classes\Installer\Dependencies\Microsoft.VS.VC_RuntimeMinimumVSU_amd64,v14');
end;

procedure InitializeWizard();
begin
  DotNetMissing := not IsDotNet6Installed();

  if DotNetMissing then
  begin
    PrereqPage := CreateOutputMsgMemoPage(wpWelcome,
      '必要なコンポーネント', '.NET 6.0 Runtime が必要です',
      'このアプリケーションを実行するには .NET 6.0 Desktop Runtime が必要です。' + #13#10 +
      'セットアップを続行する前に、Microsoft公式サイトから .NET 6.0 Desktop Runtime をダウンロードしてインストールしてください。' + #13#10#13#10 +
      'ダウンロードURL:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet/6.0' + #13#10#13#10 +
      '.NET 6.0 Desktop Runtime をインストール後、このセットアップを再実行してください。',
      '');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if DotNetMissing then
  begin
    Result := '.NET 6.0 Desktop Runtime がインストールされていません。' + #13#10 +
              'セットアップを続行できません。';
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if DotNetMissing and (PageID > PrereqPage.ID) then
    Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Create initial configuration
    SaveStringToFile(ExpandConstant('{userappdata}\MurtiWifiConnecter\first_run.txt'),
                     'This is the first installation. The setup wizard will run on first launch.', False);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  // Check Windows version
  if not IsWin64 then
  begin
    MsgBox('このアプリケーションには 64-bit Windows が必要です。', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  // Check if older version is running
  if CheckForMutexes('MurtiWifiConnecterMutex') then
  begin
    if MsgBox('MurtiWifi Connector が実行中です。アプリケーションを終了してからセットアップを続行してください。' + #13#10#13#10 +
              'アプリケーションを終了しますか？', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // Attempt to close the application
      if not Exec(ExpandConstant('{cmd}'), '/c taskkill /f /im MurtiWifiConnecter.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox('アプリケーションを終了できませんでした。手動で終了してからセットアップを再実行してください。', mbError, MB_OK);
        Result := False;
      end;
    end else
    begin
      Result := False;
    end;
  end;
end;

procedure InitializeUninstallProgressForm();
begin
  UninstallProgressForm.Caption := 'MurtiWifi Connector のアンインストール';
  UninstallProgressForm.StatusLabel.Caption := 'MurtiWifi Connector をアンインストールしています...';
end;

[Messages]
japanese.WelcomeLabel2=このウィザードは [name] をコンピュータにインストールします。%n%n個人・家庭向けWiFi管理アプリケーション「MurtiWifi Connector」へようこそ！%n%n続行する前に、他のアプリケーションをすべて終了することをお勧めします。
japanese.ClickNext=続行するには [次へ] をクリックしてください。
japanese.FinishedLabelNoIcons=[name] のインストールが完了しました。%n%nシステムトレイからアプリケーションにアクセスできます。
english.WelcomeLabel2=This will install [name] on your computer.%n%nWelcome to MurtiWifi Connector - Personal & Family WiFi Management!%n%nIt is recommended that you close all other applications before continuing.

[CustomMessages]
japanese.LaunchProgram=MurtiWifi Connector を実行
english.LaunchProgram=Launch MurtiWifi Connector
japanese.CreateDesktopIcon=デスクトップにアイコンを作成(&D)
english.CreateDesktopIcon=Create a &desktop icon
japanese.CreateQuickLaunchIcon=クイック起動アイコンを作成(&Q)
english.CreateQuickLaunchIcon=Create a &Quick Launch icon
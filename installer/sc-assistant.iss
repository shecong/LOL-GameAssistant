#define AppName "sc助手"
#define Publisher "shecong"
#define ProjectUrl "https://github.com/shecong/LOL-GameAssistant"

[Setup]
AppId={{79768BC4-A36C-443F-A52A-10513520C82B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
AppCopyright=Copyright © 2026 shecong and project contributors
VersionInfoCopyright=Copyright © 2026 shecong and project contributors
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#Publisher}
VersionInfoDescription=sc Assistant Setup
DefaultDirName={autopf}\{#AppName}
DisableDirPage=no
UsePreviousAppDir=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\LOL-GameAssistant\Resources\Brand\sc-icon.ico
PrivilegesRequired=admin
#if RuntimeArch == "x86"
ArchitecturesAllowed=x86compatible
#elif RuntimeArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir={#OutputDirectory}
OutputBaseFilename=sc-assistant-setup-{#AppVersion}-win-{#RuntimeArch}

[Languages]
Name: "chinesesimp"; MessagesFile: "{#ChineseMessages}"; InfoBeforeFile: "copyright.zh-CN.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "copyright.en.txt"

[Messages]
chinesesimp.WizardInfoBefore=版权声明
english.WizardInfoBefore=Copyright Notice

[CustomMessages]
chinesesimp.CreateDesktopShortcut=创建桌面快捷方式
english.CreateDesktopShortcut=Create a desktop shortcut
chinesesimp.LaunchAssistant=启动 sc助手
english.LaunchAssistant=Launch sc Assistant
chinesesimp.RuntimeTitle=正在准备 .NET 桌面运行时
english.RuntimeTitle=Preparing the .NET Desktop Runtime
chinesesimp.RuntimeDescription=本机缺少所需版本，正在从微软下载并校验。
english.RuntimeDescription=The required version is missing. Setup is downloading and verifying it from Microsoft.
chinesesimp.RuntimeFailed=无法准备 .NET 11 桌面运行时（{#RuntimeArch}）。请检查网络，或从微软安装运行时后重试。%n%n%1
english.RuntimeFailed=Setup could not prepare the .NET 11 Desktop Runtime ({#RuntimeArch}). Check your connection or install the runtime from Microsoft and try again.%n%n%1
chinesesimp.RuntimeExitCode=运行时安装程序返回错误 %1。
english.RuntimeExitCode=The runtime installer returned error %1.
chinesesimp.RuntimeMissing=运行时安装完成后仍未检测到所需版本。
english.RuntimeMissing=The required runtime was not detected after installation.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "copyright.zh-CN.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "copyright.en.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "{cm:LaunchAssistant}"; Flags: nowait postinstall skipifsilent; Check: CanLaunchAssistant

[Code]
var
  RuntimePage: TDownloadWizardPage;
  RuntimeRestartRequired: Boolean;

function HasFramework(const Root, FrameworkName, RequiredLibrary: String): Boolean;
var
  FrameworkDir: String;
  Entry: TFindRec;
  Patch: Integer;
begin
  Result := False;
  FrameworkDir := AddBackslash(Root) + 'shared\' + FrameworkName + '\';
  if not FindFirst(FrameworkDir + '11.0.*', Entry) then Exit;
  try
    repeat
      if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then begin
        Patch := StrToIntDef(Copy(Entry.Name, 6, Length(Entry.Name)), -1);
        if (Patch >= {#RuntimeMinimumPatch}) and
          FileExists(FrameworkDir + Entry.Name + '\' + RequiredLibrary) then begin
          Result := True;
          Exit;
        end;
      end;
    until not FindNext(Entry);
  finally
    FindClose(Entry);
  end;
end;

function HasDesktopRuntimeAt(const Root: String): Boolean;
begin
  Result := (Root <> '') and
    HasFramework(Root, 'Microsoft.WindowsDesktop.App', 'PresentationFramework.dll') and
    HasFramework(Root, 'Microsoft.NETCore.App', 'coreclr.dll');
end;

function HasDesktopRuntime: Boolean;
var
  Root: String;
begin
  Root := GetEnv('DOTNET_ROOT_' + Uppercase('{#RuntimeArch}'));
#if RuntimeArch == "x86"
  if (Root = '') and IsWin64 then Root := GetEnv('DOTNET_ROOT(x86)');
#endif
  if Root = '' then Root := GetEnv('DOTNET_ROOT');
  if Root <> '' then begin
    Result := HasDesktopRuntimeAt(Root);
    Exit;
  end;
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\{#RuntimeArch}', 'InstallLocation', Root) then begin
    Root := ExpandConstant('{autopf}\dotnet');
#if RuntimeArch == "x64"
    if IsArm64 then Root := Root + '\x64';
#endif
  end;
  Result := HasDesktopRuntimeAt(Root);
end;

procedure InitializeWizard;
begin
  RuntimePage := CreateDownloadPage(CustomMessage('RuntimeTitle'), CustomMessage('RuntimeDescription'), nil);
  RuntimePage.ShowBaseNameInsteadOfUrl := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasDesktopRuntime then begin
    Log('Compatible .NET Desktop Runtime already installed.');
    Exit;
  end;
  RuntimePage.Clear;
  RuntimePage.Add('{#RuntimeUrl}', '{#RuntimeInstaller}', '{#RuntimeSha256}');
  RuntimePage.Show;
  try
    try
      RuntimePage.Download;
      if not Exec(ExpandConstant('{tmp}\{#RuntimeInstaller}'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
        Result := FmtMessage(CustomMessage('RuntimeFailed'), [SysErrorMessage(ExitCode)]);
        Exit;
      end;
      if ExitCode = 3010 then begin
        RuntimeRestartRequired := True;
        Exit;
      end;
      if ExitCode <> 0 then begin
        Result := FmtMessage(CustomMessage('RuntimeFailed'), [FmtMessage(CustomMessage('RuntimeExitCode'), [IntToStr(ExitCode)])]);
        Exit;
      end;
      if not HasDesktopRuntime then
        Result := FmtMessage(CustomMessage('RuntimeFailed'), [CustomMessage('RuntimeMissing')]);
    except
      Result := FmtMessage(CustomMessage('RuntimeFailed'), [GetExceptionMessage]);
    end;
  finally
    RuntimePage.Hide;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeRestartRequired;
end;

function CanLaunchAssistant: Boolean;
begin
  Result := not RuntimeRestartRequired;
end;

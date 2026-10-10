; JianYiImage installer (Inno Setup 6.5 or newer).
;
;   ISCC.exe /DVersion=1.1.0 /DVersionNumeric=1.1.0.0 /DPublishDir=<publish folder> /O<output folder> JianYiImage.iss
;
; The OCR language list (components and the lookup table in [Code]) mirrors src/JianYiImage.Core/Ocr/OcrLanguageCatalog.cs;
; a unit test fails when the two drift apart. Windows installs the packs itself ("Features on Demand"), so the wizard
; only decides which ones to ask for. They are installed by a short elevated PowerShell run (OcrLanguages.ps1) at the end of
; the installation; the application itself is installed per user and needs no administrator rights.

#ifndef Version
  #define Version "0.0.0-dev"
#endif
#ifndef VersionNumeric
  #define VersionNumeric "0.0.0.0"
#endif
#ifndef PublishDir
  #error PublishDir is required: pass /DPublishDir=<the folder produced by dotnet publish>
#endif

#define AppName "简壹图片浏览器"
#define AppExe "JianYiImage.exe"
#define AppUrl "https://github.com/loonslo/JianYiImage"

[Setup]
AppId={{5D2A7C4E-93B1-4F60-8A3E-1C6E0B7D2F94}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher=loonslo
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#VersionNumeric}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} 安装程序
DefaultDirName={autopf}\JianYiImage
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=JianYiImage-v{#Version}-win-x64-setup
SetupIconFile=..\src\JianYiImage.App\Assets\Jianyi.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
#ifdef FastBuild
Compression=zip/1
SolidCompression=no
#else
Compression=lzma2/ultra64
SolidCompression=yes
#endif
WizardStyle=modern
#ifdef WizardImageDir
WizardImageFile={#WizardImageDir}\wizard-large.bmp,{#WizardImageDir}\wizard-large-150.bmp,{#WizardImageDir}\wizard-large-200.bmp
WizardSmallImageFile={#WizardImageDir}\wizard-small.bmp,{#WizardImageDir}\wizard-small-150.bmp,{#WizardImageDir}\wizard-small-200.bmp
#endif
ShowComponentSizes=no
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "zh"; MessagesFile: "ChineseSimplified.isl"

[Types]
Name: "recommended"; Description: "推荐安装（简体中文 + English 识别）"
Name: "minimal"; Description: "仅安装程序（不安装 OCR 语言包）"
Name: "custom"; Description: "自定义（自行勾选要安装的 OCR 语言包）"; Flags: iscustom

[Components]
Name: "app"; Description: "简壹图片浏览器"; Types: recommended minimal custom; Flags: fixed
Name: "ocr"; Description: "OCR 识别语言包（Windows 系统组件，需联网下载；安装时会请求一次管理员授权）"
Name: "ocr\common"; Description: "常用语言"
Name: "ocr\common\zh_cn"; Description: "简体中文"; Types: recommended custom
Name: "ocr\common\en_us"; Description: "English（美国英语）"; Types: recommended custom
Name: "ocr\common\zh_tw"; Description: "繁體中文（台灣）"; Types: custom
Name: "ocr\common\ja_jp"; Description: "日本語（日语）"; Types: custom
Name: "ocr\common\ko_kr"; Description: "한국어（韩语）"; Types: custom
Name: "ocr\other"; Description: "其他语言"
Name: "ocr\other\en_gb"; Description: "English（英国英语）"; Types: custom
Name: "ocr\other\zh_hk"; Description: "繁體中文（香港）"; Types: custom
Name: "ocr\other\fr_fr"; Description: "Français（法语）"; Types: custom
Name: "ocr\other\de_de"; Description: "Deutsch（德语）"; Types: custom
Name: "ocr\other\es_es"; Description: "Español（西班牙语·西班牙）"; Types: custom
Name: "ocr\other\es_mx"; Description: "Español（西班牙语·墨西哥）"; Types: custom
Name: "ocr\other\it_it"; Description: "Italiano（意大利语）"; Types: custom
Name: "ocr\other\pt_br"; Description: "Português（葡萄牙语·巴西）"; Types: custom
Name: "ocr\other\pt_pt"; Description: "Português（葡萄牙语·葡萄牙）"; Types: custom
Name: "ocr\other\ru_ru"; Description: "Русский（俄语）"; Types: custom
Name: "ocr\other\tr_tr"; Description: "Türkçe（土耳其语）"; Types: custom
Name: "ocr\other\nl_nl"; Description: "Nederlands（荷兰语）"; Types: custom
Name: "ocr\other\pl_pl"; Description: "Polski（波兰语）"; Types: custom
Name: "ocr\other\sv_se"; Description: "Svenska（瑞典语）"; Types: custom
Name: "ocr\other\da_dk"; Description: "Dansk（丹麦语）"; Types: custom
Name: "ocr\other\nb_no"; Description: "Norsk bokmål（挪威语）"; Types: custom
Name: "ocr\other\fi_fi"; Description: "Suomi（芬兰语）"; Types: custom
Name: "ocr\other\cs_cz"; Description: "Čeština（捷克语）"; Types: custom
Name: "ocr\other\hu_hu"; Description: "Magyar（匈牙利语）"; Types: custom
Name: "ocr\other\el_gr"; Description: "Ελληνικά（希腊语）"; Types: custom
Name: "ocr\other\bg_bg"; Description: "Български（保加利亚语）"; Types: custom
Name: "ocr\other\ar_sa"; Description: "العربية（阿拉伯语）"; Types: custom

[Tasks]
Name: "associations"; Description: "添加到资源管理器右键菜单和「打开方式」"; GroupDescription: "文件关联："
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Components: app; Flags: ignoreversion
Source: "OcrLanguages.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Components: app
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Components: app; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--register-file-associations"; Flags: runhidden runasoriginaluser; Tasks: associations; StatusMsg: "正在添加文件关联..."
Filename: "{app}\{#AppExe}"; Description: "运行{#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--unregister-file-associations --only-if-registered-here"; Flags: runhidden; RunOnceId: "UnregisterFileAssociations"

[Code]
type
  TOcrLang = record
    Component: String;      // component name in [Components]
    Caption: String;        // the component's description, which is also its text in the wizard
    CapabilityTag: String;  // for Add-WindowsCapability, e.g. zh-CN
    EngineTag: String;      // what the Windows OCR engine reports, e.g. zh-Hans-CN
  end;

var
  OcrLangs: array of TOcrLang;
  InstalledEngineTags: String;   // '|zh-hans-cn|en-us|' in lower case, for Pos() lookups
  EngineTagsLoaded: Boolean;
  CaptionsMarked: Boolean;
  OcrNote: String;               // what to tell the user on the last page

procedure AddOcrLang(const Component, Caption, CapabilityTag, EngineTag: String);
var
  N: Integer;
begin
  N := GetArrayLength(OcrLangs);
  SetArrayLength(OcrLangs, N + 1);
  OcrLangs[N].Component := Component;
  OcrLangs[N].Caption := Caption;
  OcrLangs[N].CapabilityTag := CapabilityTag;
  OcrLangs[N].EngineTag := EngineTag;
end;

function PowerShellExe: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function ScriptParams(const Mode, ResultFile, Extra: String): String;
begin
  Result := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\OcrLanguages.ps1') + '" -Mode ' + Mode +
    ' ' + Extra + ' -OutFile "' + ResultFile + '"';
end;

// Which OCR languages does Windows offer right now? (no elevation needed)
procedure LoadInstalledEngineTags;
var
  ResultFile: String;
  Lines: TArrayOfString;
  ResultCode, I: Integer;
begin
  if EngineTagsLoaded then
    Exit;
  EngineTagsLoaded := True;
  InstalledEngineTags := '|';
  try
    ExtractTemporaryFile('OcrLanguages.ps1');
    ResultFile := ExpandConstant('{tmp}\ocr-installed.txt');
    DeleteFile(ResultFile);
    if Exec(PowerShellExe, ScriptParams('List', ResultFile, ''), '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
       and (ResultCode = 0) and LoadStringsFromFile(ResultFile, Lines) then
    begin
      for I := 0 to GetArrayLength(Lines) - 1 do
        if Trim(Lines[I]) <> '' then
          InstalledEngineTags := InstalledEngineTags + Lowercase(Trim(Lines[I])) + '|';
    end;
  except
    Log('Could not list the installed OCR languages: ' + GetExceptionMessage);
  end;
  Log('OCR languages already on this PC: ' + InstalledEngineTags);
end;

function IsOcrInstalled(const EngineTag: String): Boolean;
begin
  Result := Pos('|' + Lowercase(EngineTag) + '|', InstalledEngineTags) > 0;
end;

function FindListItem(const Caption: String): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to WizardForm.ComponentsList.Items.Count - 1 do
    if WizardForm.ComponentsList.ItemCaption[I] = Caption then
    begin
      Result := I;
      Exit;
    end;
end;

// Tell the user which packs Windows already has, so they only tick what is missing.
procedure MarkInstalledLanguages;
var
  I, Index: Integer;
begin
  if CaptionsMarked then
    Exit;
  CaptionsMarked := True;
  LoadInstalledEngineTags;
  for I := 0 to GetArrayLength(OcrLangs) - 1 do
    if IsOcrInstalled(OcrLangs[I].EngineTag) then
    begin
      Index := FindListItem(OcrLangs[I].Caption);
      if Index >= 0 then
        WizardForm.ComponentsList.ItemCaption[Index] := OcrLangs[I].Caption + '  [系统已有]';
    end;
end;

// Selected packs that Windows does not have yet, as a comma separated list of capability tags.
function PendingCapabilityTags: String;
var
  I: Integer;
begin
  Result := '';
  LoadInstalledEngineTags;
  for I := 0 to GetArrayLength(OcrLangs) - 1 do
    if WizardIsComponentSelected(OcrLangs[I].Component) and not IsOcrInstalled(OcrLangs[I].EngineTag) then
    begin
      if Result <> '' then
        Result := Result + ',';
      Result := Result + OcrLangs[I].CapabilityTag;
    end;
end;

procedure AddToList(var List: String; const Item: String);
begin
  if List <> '' then
    List := List + '、';
  List := List + Item;
end;

// The captions of the selected packs Windows does not have yet, for the confirmation before the elevation prompt.
function PendingCaptions: String;
var
  I: Integer;
begin
  Result := '';
  LoadInstalledEngineTags;
  for I := 0 to GetArrayLength(OcrLangs) - 1 do
    if WizardIsComponentSelected(OcrLangs[I].Component) and not IsOcrInstalled(OcrLangs[I].EngineTag) then
      AddToList(Result, OcrLangs[I].Caption);
end;

function CaptionOfTag(const CapabilityTag: String): String;
var
  I: Integer;
begin
  Result := CapabilityTag;
  for I := 0 to GetArrayLength(OcrLangs) - 1 do
    if OcrLangs[I].CapabilityTag = CapabilityTag then
    begin
      Result := OcrLangs[I].Caption;
      Exit;
    end;
end;

// One short paragraph from the lines the helper script wrote (tag|status|detail).
function SummaryFromResultFile(const ResultFile: String): String;
var
  Lines: TArrayOfString;
  I, P1, P2: Integer;
  Line, Tag, Status, Detail, Installed, Missing, Failed: String;
begin
  Result := '';
  if not LoadStringsFromFile(ResultFile, Lines) then
    Exit;
  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    P1 := Pos('|', Line);
    if P1 = 0 then
      Continue;
    Tag := Copy(Line, 1, P1 - 1);
    Line := Copy(Line, P1 + 1, Length(Line));
    P2 := Pos('|', Line);
    if P2 = 0 then
    begin
      Status := Line;
      Detail := '';
    end
    else
    begin
      Status := Copy(Line, 1, P2 - 1);
      Detail := Copy(Line, P2 + 1, Length(Line));
    end;
    if (Status = 'installed') or (Status = 'already') then
      AddToList(Installed, CaptionOfTag(Tag))
    else if Status = 'unavailable' then
      AddToList(Missing, CaptionOfTag(Tag))
    else
      AddToList(Failed, CaptionOfTag(Tag) + '（' + Detail + '）');
  end;
  if Installed <> '' then
    Result := Result + '已就绪：' + Installed + '。';
  if Missing <> '' then
    Result := Result + #13#10 + '当前 Windows 不提供：' + Missing + '。';
  if Failed <> '' then
    Result := Result + #13#10 + '没有装上：' + Failed + '。请连接网络后重新运行安装程序，或参见项目主页的“OCR 语言包”一节。';
end;

procedure InstallOcrLanguages;
var
  Tags, ResultFile, Summary: String;
  ResultCode: Integer;
begin
  Tags := PendingCapabilityTags;
  if Tags = '' then
  begin
    Log('No OCR language pack needs to be installed.');
    Exit;
  end;

  if not WizardSilent then
  begin
    if MsgBox('接下来安装 OCR 语言包：' + #13#10 + PendingCaptions + #13#10#13#10 +
              'Windows 会弹出一次“用户账户控制”窗口，请点“是”授权。语言包需要联网下载，可能需要几分钟。' + #13#10#13#10 +
              '现在安装吗？选“否”会跳过语言包（程序本身不受影响，以后重新运行安装程序即可补装）。',
              mbConfirmation, MB_YESNO) <> IDYES then
    begin
      OcrNote := 'OCR 语言包没有安装（你选择了跳过）。需要时重新运行安装程序并勾选语言包即可。';
      Log('The user skipped the OCR language packs: ' + Tags);
      Exit;
    end;
  end;

  ExtractTemporaryFile('OcrLanguages.ps1');
  ResultFile := ExpandConstant('{tmp}\ocr-install.txt');
  DeleteFile(ResultFile);
  WizardForm.StatusLabel.Caption := '正在安装 OCR 语言包（需要联网，可能需要几分钟）...';
  WizardForm.FilenameLabel.Caption := '';
  Log('Installing OCR language packs: ' + Tags);

  // "runas" raises one UAC prompt, or none when this setup already runs elevated.
  if not ShellExec('runas', PowerShellExe, ScriptParams('Install', ResultFile, '-Languages ' + Tags), '', SW_HIDE,
                   ewWaitUntilTerminated, ResultCode) then
  begin
    OcrNote := '没有获得管理员授权，OCR 语言包没有安装（程序本身已装好）。' + #13#10 +
               '需要时重新运行安装程序并勾选语言包即可。';
    Log('Elevation refused or failed, code ' + IntToStr(ResultCode));
    if not WizardSilent then
      MsgBox(OcrNote, mbInformation, MB_OK);
    Exit;
  end;

  Summary := SummaryFromResultFile(ResultFile);
  Log('OCR language pack result (exit code ' + IntToStr(ResultCode) + '): ' + Summary);
  if Summary = '' then
    Summary := 'OCR 语言包没有返回结果，请查看安装日志，或重新运行安装程序再试一次。';
  OcrNote := 'OCR 语言包：' + #13#10 + Summary;
  if (ResultCode <> 0) and (not WizardSilent) then
    MsgBox(OcrNote, mbInformation, MB_OK);
end;

procedure InitializeWizard;
begin
  AddOcrLang('ocr\common\zh_cn', '简体中文', 'zh-CN', 'zh-Hans-CN');
  AddOcrLang('ocr\common\en_us', 'English（美国英语）', 'en-US', 'en-US');
  AddOcrLang('ocr\common\zh_tw', '繁體中文（台灣）', 'zh-TW', 'zh-Hant-TW');
  AddOcrLang('ocr\common\ja_jp', '日本語（日语）', 'ja-JP', 'ja-JP');
  AddOcrLang('ocr\common\ko_kr', '한국어（韩语）', 'ko-KR', 'ko-KR');
  AddOcrLang('ocr\other\en_gb', 'English（英国英语）', 'en-GB', 'en-GB');
  AddOcrLang('ocr\other\zh_hk', '繁體中文（香港）', 'zh-HK', 'zh-Hant-HK');
  AddOcrLang('ocr\other\fr_fr', 'Français（法语）', 'fr-FR', 'fr-FR');
  AddOcrLang('ocr\other\de_de', 'Deutsch（德语）', 'de-DE', 'de-DE');
  AddOcrLang('ocr\other\es_es', 'Español（西班牙语·西班牙）', 'es-ES', 'es-ES');
  AddOcrLang('ocr\other\es_mx', 'Español（西班牙语·墨西哥）', 'es-MX', 'es-MX');
  AddOcrLang('ocr\other\it_it', 'Italiano（意大利语）', 'it-IT', 'it-IT');
  AddOcrLang('ocr\other\pt_br', 'Português（葡萄牙语·巴西）', 'pt-BR', 'pt-BR');
  AddOcrLang('ocr\other\pt_pt', 'Português（葡萄牙语·葡萄牙）', 'pt-PT', 'pt-PT');
  AddOcrLang('ocr\other\ru_ru', 'Русский（俄语）', 'ru-RU', 'ru-RU');
  AddOcrLang('ocr\other\tr_tr', 'Türkçe（土耳其语）', 'tr-TR', 'tr-TR');
  AddOcrLang('ocr\other\nl_nl', 'Nederlands（荷兰语）', 'nl-NL', 'nl-NL');
  AddOcrLang('ocr\other\pl_pl', 'Polski（波兰语）', 'pl-PL', 'pl-PL');
  AddOcrLang('ocr\other\sv_se', 'Svenska（瑞典语）', 'sv-SE', 'sv-SE');
  AddOcrLang('ocr\other\da_dk', 'Dansk（丹麦语）', 'da-DK', 'da-DK');
  AddOcrLang('ocr\other\nb_no', 'Norsk bokmål（挪威语）', 'nb-NO', 'nb-NO');
  AddOcrLang('ocr\other\fi_fi', 'Suomi（芬兰语）', 'fi-FI', 'fi-FI');
  AddOcrLang('ocr\other\cs_cz', 'Čeština（捷克语）', 'cs-CZ', 'cs-CZ');
  AddOcrLang('ocr\other\hu_hu', 'Magyar（匈牙利语）', 'hu-HU', 'hu-HU');
  AddOcrLang('ocr\other\el_gr', 'Ελληνικά（希腊语）', 'el-GR', 'el-GR');
  AddOcrLang('ocr\other\bg_bg', 'Български（保加利亚语）', 'bg-BG', 'bg-BG');
  AddOcrLang('ocr\other\ar_sa', 'العربية（阿拉伯语）', 'ar-SA', 'ar-SA');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectComponents then
    MarkInstalledLanguages
  else if (CurPageID = wpFinished) and (OcrNote <> '') then
  begin
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + OcrNote;
    WizardForm.FinishedLabel.AdjustHeight;       // the label has a fixed height; measure the wrapped text
    // The check box list ("run the program") was positioned for the original text; move it below the taller label.
    WizardForm.RunList.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(16);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallOcrLanguages;
end;

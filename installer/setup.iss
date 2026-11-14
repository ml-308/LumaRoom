; LumaRoom · 安装程序（Inno Setup 6）
; 页面结构与选项由 common.iss 统一提供；这里只声明应用身份与要安装的文件。
; 构建：installer\build-setup.cmd   产物：dist\LumaRoom-Setup-1.2.0.exe

#define AppName "LumaRoom"
#define AppSlug "LumaRoom"
#define DirName "LumaRoom"
#define AppVersion "1.2.0"
#define AppId "{{C4D81F62-0A37-4B9E-8F25-7B3E6C19A082}"
#define AppExe "LumaRoom.exe"
#define AppRepo "LumaRoom"
#define AppTagline "轻量图像暗房：影调调整、RAW 降噪、蒙版局部编辑，单文件零运行时依赖。"
#define IconPath "app.ico"

; 图像处理工具：注册到「打开方式」，双击照片就能进来
#define FeatureFileAssoc

#include "common.iss"

[Files]
Source: "{#AppRoot}\LumaRoom.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppRoot}\README.md"; DestDir: "{app}"
Source: "{#AppRoot}\LICENSE"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; 只声明支持的扩展名，让系统把这些类型列进「打开方式」；不改任何默认程序
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".tif"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".tiff"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".nef"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cr2"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".arw"; ValueData: ""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".dng"; ValueData: ""; Tasks: fileassoc


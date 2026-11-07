@echo off
rem ============================================================
rem  LumaRoom - build script  (.NET Framework csc, no deps)
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo [ERROR] csc.exe not found & exit /b 1 )

echo [1/2] Compile LumaRoom.exe ...
"%CSC%" -nologo -target:winexe -optimize+ -unsafe+ -platform:anycpu ^
  -out:"LumaRoom.exe" ^
  -win32manifest:"app.manifest" -win32icon:"app.ico" ^
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
  "src\*.cs"
if errorlevel 1 ( echo [ERROR] compile failed & exit /b 1 )

echo [2/2] Done -^> LumaRoom.exe
endlocal

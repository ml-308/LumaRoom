@echo off
rem Build the unified installer (Inno Setup 6). Output goes to dist\.
setlocal
cd /d "%~dp0"

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo [ERROR] Inno Setup 6 not found.
  echo         winget install --id JRSoftware.InnoSetup --silent
  exit /b 1
)

"%ISCC%" "installer\setup.iss"
if errorlevel 1 (
  echo [ERROR] compile failed
  exit /b 1
)
echo [OK] installer built into dist\

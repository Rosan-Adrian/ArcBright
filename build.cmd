@echo off
rem Builds bin\ArcBright.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem Nothing to install.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find %CSC%
  exit /b 1
)
if not exist bin mkdir bin

if not exist assets\icon.ico (
  "%CSC%" /nologo /target:exe /out:bin\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs src\IconArt.cs src\Native.cs || exit /b 1
  bin\MakeIcon.exe assets\icon.ico assets\icon.png || exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /out:bin\ArcBright.exe ^
  /win32icon:assets\icon.ico /win32manifest:src\app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs || exit /b 1

echo Built bin\ArcBright.exe

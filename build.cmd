@echo off
rem Builds dist\Tandem.exe with the C# compiler that ships with Windows (.NET Framework 4).
rem Nothing to install: no Visual Studio, no SDK, no packages.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The .NET Framework 4 compiler was not found on this PC.
  exit /b 1
)

if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /warn:4 /out:dist\Tandem.exe ^
  /win32icon:assets\app.ico /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /r:System.Management.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll ^
  src\*.cs
if errorlevel 1 exit /b 1

echo Built dist\Tandem.exe

@echo off
rem Builds ResGUI.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
"%FW%\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /out:"%~dp0ResGUI.exe" /win32icon:"%~dp0ResGUI.ico" ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll ^
  "%~dp0ResGUI.cs"
if errorlevel 1 (echo Build failed. & exit /b 1)
echo Built ResGUI.exe

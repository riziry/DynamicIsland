@echo off
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WM=%WINDIR%\System32\WinMetadata
cd /d "%~dp0"
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:x64 /out:DynamicIsland.exe /win32icon:DynamicIsland.ico ^
 /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
 /r:System.Xaml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Security.dll ^
 /r:System.Core.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
 /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Threading.Tasks.dll" ^
 /r:"%FW%\System.IO.dll" /r:"%FW%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
 /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Storage.winmd" /r:"%WM%\Windows.Devices.winmd" /r:"%WM%\Windows.UI.winmd" /r:"%WM%\Windows.ApplicationModel.winmd" ^
 Island.cs Pages.cs Core.cs Settings.cs Services.cs Ai.cs Launcher.cs Games.cs Notify.cs Installer.cs Wizard.cs EasterEgg.cs Updater.cs WinKeys.cs Motion.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo OK: DynamicIsland.exe

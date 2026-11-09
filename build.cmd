@echo off
rem Builds "dist\Frontend Injector.exe" - one self-contained exe, no SDK required.
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Cannot find the .NET Framework compiler at "%CSC%".
  exit /b 1
)
if not exist dist mkdir dist

"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /codepage:65001 ^
  "/out:dist\Frontend Injector.exe" ^
  /win32icon:assets\app.ico ^
  /win32manifest:src\app.manifest ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.IO.Compression.dll ^
  /reference:System.IO.Compression.FileSystem.dll ^
  /reference:System.Web.Extensions.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:lib\Microsoft.Web.WebView2.Core.dll ^
  /reference:lib\Microsoft.Web.WebView2.WinForms.dll ^
  /resource:lib\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll ^
  /resource:lib\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll ^
  /resource:lib\WebView2Loader.dll,WebView2Loader.dll ^
  /resource:assets\app.ico,app.ico ^
  /resource:web\index.html,index.html ^
  /resource:web\app.css,app.css ^
  /resource:web\app.js,app.js ^
  /resource:web\homenx.jpg,homenx.jpg ^
  /resource:web\cardsnx.jpg,cardsnx.jpg ^
  /resource:web\nxtoggle.jpg,nxtoggle.jpg ^
  src\Program.cs src\Injector.cs src\Wpd.cs

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Built dist\Frontend Injector.exe
endlocal

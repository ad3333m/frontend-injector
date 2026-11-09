@echo off
rem Runs the copy rules against a folder standing in for the SD card.
setlocal
cd /d "%~dp0.."
start "" /wait "dist\Frontend Injector.exe" --selftest
set RESULT=%errorlevel%
type "%TEMP%\frontend-injector-selftest.txt"
echo.
exit /b %RESULT%

@echo off
setlocal
cd /d "%~dp0"
call build.cmd
if errorlevel 1 exit /b 1
set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist build mkdir build
"%CSC%" /nologo /target:exe /main:NativeSmokeTest /out:build\NativeSmokeTest.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\AccountLockoutNative.cs tests\NativeSmokeTest.cs
if errorlevel 1 exit /b 1
build\NativeSmokeTest.exe dist\AccountLockoutManager.exe
exit /b %ERRORLEVEL%

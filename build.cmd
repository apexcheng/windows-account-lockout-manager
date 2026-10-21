@echo off
setlocal
cd /d "%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: .NET Framework C# compiler not found: %CSC%
  exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:anycpu /win32manifest:src\AccountLockout.manifest /out:dist\AccountLockoutManager.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\AccountLockoutNative.cs
if errorlevel 1 exit /b 1
echo Built: dist\AccountLockoutManager.exe
endlocal

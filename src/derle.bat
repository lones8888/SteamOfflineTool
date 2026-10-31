@echo off
setlocal
rem derle.bat: builds ..\app\SteamOfflineTool.exe from SteamOfflineTool.cs with the .NET Framework 4 compiler that ships with Windows (no download).
rem The language level is C# 5 (that old csc). Close the running program first, otherwise the exe cannot be overwritten.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :yok
echo Building SteamOfflineTool.exe ...
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu /win32icon:"%~dp0SteamOfflineTool.ico" /out:"%~dp0..\app\SteamOfflineTool.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.ServiceProcess.dll /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll "%~dp0SteamOfflineTool.cs"
if errorlevel 1 goto :hata
echo DONE: ..\app\SteamOfflineTool.exe
goto :son
:yok
echo ERROR: .NET Framework 4 compiler not found.
goto :son
:hata
echo BUILD ERROR
:son
pause

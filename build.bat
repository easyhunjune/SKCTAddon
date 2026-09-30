@echo off
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:SKCTAddon.exe SKCTAddon.cs
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)
echo Built SKCTAddon.exe

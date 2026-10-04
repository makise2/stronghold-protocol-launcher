@echo off
chcp 65001 >nul
title Build - 卫戍协议启动器 v1.2.3

set "FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
set "CSC=%FW%\csc.exe"
if not exist "%CSC%" ( set "FW=C:\Windows\Microsoft.NET\Framework\v4.0.30319" & set "CSC=%FW%\csc.exe" )
if not exist "%CSC%" ( echo [x] 找不到 csc.exe，需要 Windows 10/11 & pause & exit /b 1 )

echo 编译中...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:"%~dp0app.ico" /out:"%~dp0卫戍协议启动器.exe" ^
  /reference:"%FW%\System.dll" ^
  /reference:"%FW%\System.Core.dll" ^
  /reference:"%FW%\System.Drawing.dll" ^
  /reference:"%FW%\System.Windows.Forms.dll" ^
  /reference:"%FW%\System.IO.Compression.dll" ^
  /reference:"%FW%\System.IO.Compression.FileSystem.dll" ^
  "%~dp0Launcher.cs"

if errorlevel 1 ( echo. & echo [x] 编译失败，把上面的错误发给我 & pause & exit /b 1 )
echo. & echo [OK] 编译成功：%~dp0卫戍协议启动器.exe
echo.
echo 部署到 D 盘（可选）：
echo   copy "%~dp0卫戍协议启动器.exe" "D:\卫戍协议\卫戍协议启动器\"
pause

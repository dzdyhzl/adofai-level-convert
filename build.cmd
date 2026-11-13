@echo off
chcp 65001 >nul
rem 重新编译两个程序与安装包（使用 Windows 自带 .NET Framework 4 编译器，无需安装任何东西）
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [错误] 未找到 csc.exe（.NET Framework 4.x 自带编译器）
  exit /b 1
)
set "DIR=%~dp0"

rem 命令行版
"%CSC%" /nologo /optimize+ /platform:anycpu /warn:0 /out:"%DIR%level convert.exe" "%DIR%Core.cs" "%DIR%Cli.cs"
if errorlevel 1 (echo [失败] CLI 编译出错 & exit /b 1)
echo [完成] level convert.exe

rem 图形界面版
"%CSC%" /nologo /optimize+ /platform:anycpu /warn:0 /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%DIR%level convert GUI.exe" "%DIR%Core.cs" "%DIR%Gui.cs"
if errorlevel 1 (echo [失败] GUI 编译出错 & exit /b 1)
echo [完成] level convert GUI.exe

rem —— 安装包（内嵌 GUI/CLI/README）——
if not exist "%DIR%release" mkdir "%DIR%release"
"%CSC%" /nologo /optimize+ /platform:anycpu /warn:0 /target:winexe /out:"%DIR%release\level convert 安装包.exe" "%DIR%Installer.cs" /res:"%DIR%level convert GUI.exe",LevelConvertGui /res:"%DIR%level convert.exe",LevelConvertCli /res:"%DIR%README.md",ReadmeMd
if errorlevel 1 (echo [失败] 安装包编译出错 & exit /b 1)
echo [完成] release\level convert 安装包.exe

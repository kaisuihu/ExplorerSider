@echo off
rem ExplorerSider 发布脚本：单文件免安装 exe（self-contained，无需安装 .NET 运行时）
cd /d "%~dp0ExplorerSider"
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%~dp0..\publish"
echo.
echo 发布完成: %~dp0..\publish\ExplorerSider.exe
pause

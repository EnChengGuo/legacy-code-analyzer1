@echo off
chcp 65001 >nul
cd /d "%~dp0"
if "%~1"=="" (
  echo 用法:把 .sln 或 .csproj 檔拖曳到這個檔案上。
  echo 或在命令列執行:CodeAtlas.exe serve 你的方案.sln --open
  pause
  exit /b 1
)
echo 啟動 CodeAtlas,瀏覽器會自動開啟。結束請按 Ctrl+C 或關閉這個視窗。
CodeAtlas.exe serve "%~1" 5173 --open
pause

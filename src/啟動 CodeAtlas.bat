@echo off
chcp 65001 >nul
cd /d "%~dp0"
if "%~1"=="" (
  echo 啟動 CodeAtlas,瀏覽器會開啟「開啟專案」頁面,請在那裡輸入或選擇 .sln / .csproj 的路徑。
  echo 結束請按 Ctrl+C 或關閉這個視窗。
  CodeAtlas.exe serve --open
  pause
  exit /b
)
echo 啟動 CodeAtlas,瀏覽器會自動開啟。結束請按 Ctrl+C 或關閉這個視窗。
CodeAtlas.exe serve "%~1" 5173 --open
pause

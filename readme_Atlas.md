# CodeAtlas

C# 程式碼語意分析工具。以 Roslyn 在 .NET Framework 4.8 上直接執行,**離線、免安裝、全繁體中文**。

為「公司的大型 .NET 4.8 專案、電腦離線、不能安裝東西、只有 .NET 4.8 + Visual Studio + Copilot」這種環境而做。

## 功能

- 符號瀏覽、找參考(分讀與寫)、呼叫階層、多層影響分析
- 品質度量(CC、MI、巢狀、WMC、Ca、Ce)、死碼(分可信度)、循環相依、重複程式碼
- God Class 解剖(欄位清冊、儲存對照、UI 對照、職責分群)、冤親債主(git 歷史)
- 類別圖、關係圖、Mermaid;函式流程(循序圖、活動圖、狀態圖、資料流、呼叫樹);流程畫布
- 給 Copilot 的 AI 上下文(只給簽章,需要時再讀指定函式內文)
- `--source` 用原始碼取代 DLL

完整說明見 [`CodeAtlas原版使用說明_v0.9.pdf`](CodeAtlas原版使用說明_v0.9.pdf)(含免責聲明與尚未驗證的項目)。

## 建置與執行

需要 .NET SDK(只有「建置」才需要;執行免安裝版只需要 .NET Framework 4.8)。

```powershell
cd src
dotnet build -c Release
.\bin\Release\net48\CodeAtlas.exe
```

不加參數(或直接雙擊 exe)會啟動本機網頁,並開啟「開啟專案」頁,在那裡輸入 `.sln` / `.csproj` 的路徑、瀏覽資料夾或從最近開啟挑選。
也可以直接指定專案:`CodeAtlas.exe serve ..\samples\TestShop.sln --open`。命令列的所有指令用 `CodeAtlas.exe help` 查看。

## 目錄

| 路徑 | 內容 |
|---|---|
| `src/` | 程式碼與網頁(`Web/`)、離線函式庫(`Assets/`) |
| `samples/` | 說明文件裡所有範例用的自建測試專案 |
| `docs/` | 使用說明的原始檔(`manual.template.html`、`build_manual.py`)與最終 PDF |
| `build_dist.py` | 組出免安裝發佈資料夾與 zip |

## 第三方元件

Roslyn(Microsoft.CodeAnalysis)、Cytoscape.js、dagre、cytoscape-dagre、Mermaid,各依其授權條款使用。

## 注意

死碼、重複程式碼、職責分群、儲存/UI 對照、狀態圖、資料流等結果是依程式寫法的推測,請先讀使用說明附錄 C、D。

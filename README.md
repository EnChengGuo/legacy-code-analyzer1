# <CodeAtlas>

> 用這份 README 當範例，改成你自己的版本。repo 名稱請當成自己的作品集來命名。各節都可以改寫或刪除，只有文末的官方區塊要保留。

## 關於這個 repo

我在這個 repo 整理了 <立刻組合三支很有幫助的 Skills - git/TBD/雙菱形流程> 相關的 skills，用來解決 <公司Lagecycode離線整理需求>。


## 結構說明

| 路徑 | 內容 |
|---|---|
| `.claude/skills/` | Claude Code 格式的 skills |
| `.agents/skills/` | Codex／.agents 格式的 skills |
| `VERSION` | template 版本 |

> 以上路徑是《AI x BDD》課程平台抓取與展示 skills 的固定位置，請勿更名、搬移或刪除。

## 道館作業

<!-- 若本 repo 不是道館作業，可刪除本節。 -->

本 repo 用於繳交 <道館名稱>：

1. 我依《AI x BDD》課程平台道館頁列出的題目與完成條件，完成該館要求的 skills。
2. 我錄製了一支 3 分鐘內的 demo 影片，呈現 skill 的實際執行過程。
3. 我在《AI x BDD》課程平台的道館頁貼上本 repo 連結與影片連結，完成繳交。

## Skills 與設計想法

以下說明這個 repo 收錄的 skills，以及我設計時的考量。

<!-- 以下為範例，請替換成你自己的 skill；每個 skill 一則，維持用途／使用時機／設計想法／驗證狀態四項。 -->

### `double-diamond-principle`

- **用途**：透過發散收斂去釐清需求。
- **使用時機**：正式開發前。
- **設計想法**：需求常常不明確，此時可透過雙鑽石原則去釐清問題。
- **驗證狀態**：已在個人專案中實際使用。

### `git-conventional-commit`

- **用途**：在開發階段 要求最小開發需求，並撰寫git command 供後續追蹤。
- **使用時機**：所有開發階段。
- **設計想法**：讓開發團隊更好的追蹤開發過程。
- **驗證狀態**：已在公司及個人專案中實際使用，這是我這三個裡面最滿意的，但在作業裡他沒有分開command而是最後才一次寫滿，這點不是很喜歡。

### `trunk-based-development`

- **用途**：多人協作時 一套可以用於出版的規則。
- **使用時機**：團隊開發中 且有定期出版需求。
- **設計想法**：開發者都在單一共享分支(trunk,Git 慣稱 main)上協作,並用一組有文件記載的技巧,抵抗建立其他長命開發分支的壓力。 目的是避免合併地獄、避免弄壞 build。
- **驗證狀態**：已在個人專案中實際使用，但這應該是大專案協做才能看到成效。

## 交流

其實我平常有寫 一個CodeAtlas想要在讀書會討論，這一版我不是很滿意，但合理因為原版我花大量時間在跟Claude討論，只是沒有用skill，
讀書會本想跟水球討論原版的功能，因為即便第一版功能我也要花大量時間去整理code，同時整理同事的lagecy code真的有意義存在嗎，但後還討論完還是先交作業比較好，
我放上一份PDF是我原版的，希望之後可以討論。

---

## 官方宣告（以下區塊請保留）

本 repo 為[水球軟體學院](https://world.waterballsa.tw)《AI x BDD：規格驅動全自動化開發術》課程的學員產出，用於公告課程中道館作業的展示，並依[官方 Template](https://github.com/AI-x-BDD/aixbdd-skill-homework-template) 建立。

《AI x BDD：規格驅動全自動化開發術》課程四大特色：

1. **【軟工方法論＋結果導向】**
   只談 AI coding 不談軟工方法論，是不學無術！課程中強調結果導向的開發方法：只要訂好驗收標準，就可以 One-Shot 開發到位！
2. **【最完整的 SDD 概念與實作】**
   市面上最完整的 SDD 課程，一次教你 Skills、SDD、TDD、BDD 的知識與實踐，輕鬆實踐高效可靠全自動開發。
3. **【企業級實戰導入】**
   用企業級實踐導入高規格，不只學會工具，更知道如何舉一反三完全客製化企業專案需求。
4. **【課程教學方式與物超所值】**
   這是一堂理論、實務跟實戰三者兼具的 SDD 線上課程，用工作坊級別的學習體驗，不是只是教概念，而是直接帶著做！

[了解更多課程內容說明](https://waterballs.tw/Ghl6O)

## 開源專案：AI x BDD

水球老師把這套 AI x BDD 的方法論做成開源 pipeline skill：把產品迭代寫成 Gherkin，再一路帶到 RED → GREEN → REFACTOR。

覺得有幫助，歡迎給它一顆 Star：

[![Star Waterball-Software-Academy/aixbdd](https://img.shields.io/github/stars/Waterball-Software-Academy/aixbdd?style=social)](https://github.com/Waterball-Software-Academy/aixbdd)

## 相關問題洽詢管道

1. [水球軟體學院 LINE 官方帳號](https://lin.ee/STIsOLZ)
2. 客服信箱：[support@waterballsa.tw](mailto:support@waterballsa.tw)
3. [水球軟體學院（社群）Discord](https://discord.gg/Ymjz7NmZXn)

---

© 2026 水球球特務有限公司版權所有，侵害必究。

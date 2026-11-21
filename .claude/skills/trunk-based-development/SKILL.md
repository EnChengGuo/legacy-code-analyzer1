---
name: trunk-based-development
description: 依 trunkbaseddevelopment.com 的方法論,診斷團隊分支流程、建議如何導入或改善 Trunk-Based Development(主幹開發),並教學、回答相關問題。只要使用者提到 trunk-based、主幹開發、分支策略、branching strategy、GitFlow、GitHub flow、長命分支、功能分支活太久、合併地獄、merge hell、release 分支、cherry-pick 修 bug、feature flag/開關、branch by abstraction、PR 審查太慢、code freeze、要不要用 merge queue,或描述團隊人數/發布頻率/分支壽命並想改善流程,就使用這個 skill,即使使用者沒有明說「trunk-based」。也適用於審視某個分支流程是否違反規則、或說明這套方法的出處。
---

# trunk-based-development

把 trunkbaseddevelopment.com 的方法,用在使用者的實際情境:診斷現況、指出落差、給出可執行的下一步,並讓使用者能回原站核對。
完整的詞彙、規則、適用條件與例外在 `references/tbd-notes.md`,每段都標了來源頁面。回答前先讀它,尤其是需要引用規則或例外時。

## 為什麼這樣做
這套方法的核心是縮短「距離」(尚未進入共享分支的程式碼與主線的差距)。距離越長,越容易遲到的衝突、重工、以及合併成功卻語意互相破壞。所有規則都在服務「分歧最小、隨時可發布」這個目標,所以給建議時要說明規則背後的理由,而不是只丟規則。

## 流程

### 1. 先問清楚現況(缺的才問,已知的不重問)
- 目前分支流程(GitFlow、GitHub flow、每人一條功能分支…)
- 團隊人數與同時寫入者(含 AI agent 的話也算)
- 功能分支平均壽命、PR 從開到合併的等待時間
- 發布頻率,以及發布後 bug 在哪裡修
- build / 測試時間,以及提交前是否在本機跑完整 build

### 2. 對照規則找落差
用下表當第一輪診斷,細節與例外查 `references/tbd-notes.md` 第 4–8 節:

| 面向 | 站上建議 | 常見落差 |
|---|---|---|
| 分支壽命 | 約 2 天內 | 數週的長命分支 |
| 分支人數 | 1 人(或 1 對、1 人加其 agent) | 多人共用一條分支 |
| 風格 | 約 15 人以下可直接進 trunk;更多用短命分支+PR | 站上自承門檻有爭議,當參考 |
| 大改動 | feature flag / Branch by Abstraction,每個 commit 可上線 | 用長命分支硬撐 |
| 發布 | 高頻→從 trunk 發;低頻→發布前才切 release 分支 | 長期維護 release 分支、雙向合併 |
| 修 bug | 先在 trunk 修,再 cherry-pick 到 release | 在 release 修完再合回 |
| build | 約 1 分鐘快、10 分鐘以上慢;本機先跑再推 | 靠 CI 試錯 |
| 審查 | 數分鐘最好,超過一兩小時傷害週期 | PR 掛數天 |

### 3. 給建議
- 排出優先順序:先解**最大的一個落差**(通常是分支壽命),不要一次丟十件事。
- 大功能做不完的情境,教第一個 commit 應該是什麼:先包抽象層、行為不變;或加預設關閉的開關。後續逐日累積,最後用一個小 commit 切換。
- 提醒配套:開關兩邊都要被測、開關要定期清、build 與審查要快,否則切不小。
- 若使用者要向團隊或主管說明,給「為什麼(風險與速度)」「做不完怎麼辦」「發布怎麼辦」三段。

### 4. 標註來源與信心
- 引用站上規則時附頁面名,格式 `[頁面]`,對應 `https://trunkbaseddevelopment.com/<頁面>/`。
- 區分三種內容,不要混成站上說的:站上明示的規則 / 站上的主張或推測 / 你自己的補充或推論。
- 筆記對這幾頁只讀了標題,沒讀內文:vcs-features、vcs-choices、monorepos、expanding-contracting-monorepos、game-changers、publications;alternative-branching-models 只讀前半。使用者問到這些主題時,先到原站或原始碼確認再回答,並說明筆記未涵蓋。

## 邊界
- 站上是倡議型網站,立場鮮明。遇到使用者的情境不符合前提(瀑布式、四週以上固定迭代、必須長期支援多個舊版本給外部客戶)時,明講適用條件與例外,不要硬推。
- 團隊規模門檻是經驗值,不是定律。
- 2026 版新增的 AI coding agent 段落時間較新、論述性強,與經典規則分開講。

## 教學模式
使用者說「教我」「帶我理解」時,改用一次一題的方式:先診斷現況,講一個概念,出一個小題讓使用者用自己的話回答,再依回答補缺口;最後讓使用者練習向他人說明,當作驗收。

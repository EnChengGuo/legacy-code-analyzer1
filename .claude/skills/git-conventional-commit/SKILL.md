---
name: git-conventional-commit
description: 依 Conventional Commits 1.0.0 撰寫、檢查、修正 git commit 訊息,並判斷這次變更會讓語意化版本(SemVer)升哪一碼。只要使用者提到 commit、提交訊息、commit message、幫我寫 commit、要提交了、review 這則 commit、changelog、版本號該升幾、feat/fix/BREAKING CHANGE、commitlint、semantic-release、squash merge 的訊息,或貼出 git diff/暫存區並想提交,就使用這個 skill,即使使用者沒有明說「Conventional Commits」。也適用於改寫不合規的舊訊息、判斷該不該拆成多個 commit。
---

# git-conventional-commit

為變更寫出合規、有資訊量的 Conventional Commits 訊息,並說明它對版本號的影響。
規格來源與逐條規則見 `references/spec-rules.md`(需要核對某條規則或例外時再讀)。

## 為什麼這樣做
commit 訊息的 type 與破壞性標記,會被工具拿去決定版本號與 changelog。選錯 type,後果不是「難看」,而是版本升錯、功能在 changelog 消失。所以重點是**判斷對**,格式只是載體。

## 流程

### 1. 看清楚變更
- 使用者沒貼內容時,執行 `git status` 與 `git diff --staged`(沒有暫存內容再看 `git diff`)。
- 看一眼 `git log --oneline -10` 與有無 commitlint 設定(`commitlint.config.*`、`.commitlintrc*`、`package.json` 的 commitlint 欄位)。團隊已有的 type 清單、scope 命名與語言,優先於本 skill 的預設。
- 訊息語言跟隨該專案既有 commit 的語言;看不出來就跟隨使用者說話的語言。type 與 `BREAKING CHANGE` 一律英文。

### 2. 判斷要不要拆
一個 commit 只做一種性質的事。同時有新功能與修 bug,或功能與無關的重構,建議拆開,因為 type 只有一個,混在一起會讓另一件事從 changelog 消失。同檔案混合時可用 `git add -p` 分段暫存。拆不開時,選影響最大的 type,另一件事寫進 body。規格說的是「盡量拆」,不是強制,把理由告訴使用者,由使用者決定。

### 3. 選 type
先問「對使用這個專案的人,行為有什麼變化」:

| 變化 | type | 版本 |
|---|---|---|
| 新增能力,舊用法照舊 | `feat` | MINOR |
| 修正錯誤行為,介面不變 | `fix` | PATCH |
| 舊用法會壞(任何 type 都可能) | 加 `!` 或 `BREAKING CHANGE:` | MAJOR |
| 其餘:`docs` `style` `refactor` `perf` `test` `build` `ci` `chore` `revert` | 不升版 | 無 |

後一列的 type 名稱是 Angular / commitlint 的慣例,規格只要求 `feat`、`fix` 的語意,其他 type 允許自訂。依賴更新、開發工具調整通常是 `build` 或 `chore`。

**破壞性是「舊用法會不會壞」,不是改動大不大。** 內部重寫但對外介面不變,不是破壞性;只改一個參數名但外部會報錯,就是。

### 4. 填各個部分
```
<type>(<scope>)!: <描述>

<body>

<footer>
```
- **scope**(可省):括號內名詞,指程式碼區塊。沿用專案已用過的 scope,不要自創新詞。
- **描述**:說明「這次變更的結果」,祈使句、簡短、不加句點。冒號前緊貼,冒號後一個空格。避免 `update`、`fix bug` 這類沒資訊的寫法。
- **body**(可省):前面空一行。寫「為什麼改」與「和以前的差別」。小改動不用寫。
- **footer**(可省):前面空一行。`token: value` 或 `token #value`,token 以 `-` 取代空格,如 `Refs: #42`、`Reviewed-by: Z`。
- **破壞性變更**:`!` 與 `BREAKING CHANGE:` footer 可擇一或並用。需要交代遷移方式時用 footer。footer 的 `BREAKING CHANGE` 必須全大寫(`BREAKING-CHANGE` 同義)。沒有破壞性的 commit 不要寫 `BREAKING CHANGE: none`,工具看的是 token,不看內容,會誤升 MAJOR。

### 5. 檢查
把訊息存成暫存檔後執行腳本(只用標準庫):

```bash
uv run scripts/lint_commit.py <訊息檔>          # 或 python scripts/lint_commit.py <訊息檔>
```
- 退出碼 `0` 通過、`1` 有違反規格的錯誤、`2` 用法錯誤。
- 輸出分兩層:**規格錯誤**(必須改)與**慣例提醒**(type 不在常見清單、大小寫、句點、長度,看團隊約定決定要不要改)。
- 輸出的 `bump` 欄位就是這則訊息對版本的影響。
- 腳本只檢查結構,不判斷 type 選得對不對,那是第 3 步的工作。

### 6. 交付
把最終訊息放在 code block 給使用者,並用一兩句說明:選了什麼 type、為什麼、版本會升哪一碼。**使用者沒有要求時不要自己執行 `git commit`。**

## 常見情況
- **改寫舊訊息**:先指出規格錯誤,再指出慣例問題,最後給改好的版本。`Feat:`、`feet:` 這類規格上合規、但工具可能擋或漏掉,要分開講。
- **squash merge**:訊息看合併後的淨結果,不是過程。分支上的 `wip`、`try again` 不進訊息;type 依最終對外的影響決定。
- **選錯 type**:未發佈前用 `git rebase -i` 改訊息(僅限尚未推送或自己的分支);已發佈則不退版本、往前補正確級別,規格未規定細節,以團隊流程為準。
- **revert**:規格未定義。常見寫法是 `revert:` type 加 footer 引用被還原的 SHA。升版與否由團隊工具設定決定,先確認專案怎麼做。
- **0.x 階段**:仍照規則寫,因為已有人在用。0.x 如何對應版本取決於工具設定。
- **自訂 type**:規格允許,但版本影響與檢查要另外在 commitlint / release 工具設定,規格本身不處理。

## 不確定時
- 規則細節以 `references/spec-rules.md` 與其中的原始來源為準;不確定的部分明說「這是慣例」或「規格沒有規定」,不要編造。
- 專案已有 commitlint 設定時,以它為準,並在回答中說明你依據的是哪個設定檔。

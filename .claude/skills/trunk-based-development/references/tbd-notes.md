# Trunk-Based Development(主幹開發)學習筆記

## 來源與核對方式

- 網站:https://trunkbaseddevelopment.com (作者 Paul Hammant)
- 讀取方式:網站原始碼是公開的 Markdown,我直接讀 GitHub 倉庫 `paul-hammant/tbd` 的 `content/`,
  對應 commit `44abf38fc1215e859c3157536c0dfa5353925ce0`(與網站首頁頁尾標示的發佈 commit 相同)。
- 核對:每個段落標了 `[頁面]`。頁面網址 = `https://trunkbaseddevelopment.com/<頁面>/`。
  原始檔 = `https://github.com/paul-hammant/tbd/blob/44abf38fc1215e859c3157536c0dfa5353925ce0/content/<頁面>/index.md`
  (首頁是 `content/_index.md`)。
- 本筆記是中文轉述,非逐字翻譯。有引號的短句才是原文大意引用,以原站為準。

### 閱讀完整度(誠實聲明)

| 狀態 | 頁面 |
|---|---|
| 全文讀完 | 首頁、context、5-min-overview、deciding-factors、styles、committing-straight-to-the-trunk、short-lived-feature-branches、continuous-integration、fast-green-builds、continuous-review、continuous-delivery、release-from-trunk、branch-for-release、feature-flags、branch-by-abstraction、concurrent-development-of-consecutive-releases、strangulation、observed-habits、youre-doing-it-wrong |
| 讀了前半 | alternative-branching-models(GitHub flow、GitFlow、多 trunk、Mainline 已讀;後面的舊模型未讀) |
| 只讀標題,未讀內文 | vcs-features、vcs-choices、monorepos、expanding-contracting-monorepos、game-changers、publications |
| 未讀 | book、key、contributions |

---

## 1. 一句話定義  `[首頁]`

開發者都在單一共享分支(trunk,Git 慣稱 main)上協作,並用一組有文件記載的技巧,抵抗建立其他長命開發分支的壓力。
目的是避免合併地獄、避免弄壞 build。

- 「trunk」只是角色名稱,分支實際叫 main 也算。 `[5-min-overview]`
- 站上說的「developers」也包含 QA 自動化人員與他們指揮的 coding agent。 `[5-min-overview]`
- 光是把分支命名為 trunk 不算:若多人共用、或分支好幾天不刪,就不是 TBD。 `[youre-doing-it-wrong]`

## 2. 關鍵字詞彙表

| 詞 | 意思 | 頁面 |
|---|---|---|
| Trunk / main | 唯一的共享主線 | 首頁 |
| Distance(距離) | 尚未進入共享分支的程式碼與主線的差距;越遠越可能難合併、重工、語意衝突(出自 Frank Compagner) | 5-min-overview |
| Merge hell(合併地獄) | 長命分支晚合併造成的痛苦 | 首頁 |
| Release ready | 隨時能在短時間(文中舉例一小時)上線 | 5-min-overview |
| Short-lived feature branch | 一人(或一對)、最多約兩天、審完即合併並刪除的分支 | short-lived-feature-branches |
| Patch review | 不用分支,把修補送到 Gerrit/Phabricator 等系統審核 | styles |
| Pre-integrate build | 提交前在自己機器跑完整 build | 首頁、styles |
| CI | 原意是開發者自己頻繁整合到單一點;伺服器只是驗證安全網 | continuous-integration |
| Build cop | 負責處理壞 build、必要時鎖 trunk 的角色 | observed-habits |
| Race to push | Git 要求先 pull 才能 push,忙碌時一直輸掉競爭 | deciding-factors |
| Merge / commit queue | 機器人排隊、rebase、驗證後才落地,是序列化點 | deciding-factors、committing-straight |
| Feature flag(toggle) | 用開關控制功能啟用,未完成的碼可關閉上線 | feature-flags |
| Branch by Abstraction(BbA) | 用抽象層在主幹上完成需數天的大改動 | branch-by-abstraction |
| Strangulation | 用路由逐步以新系統取代跨語言舊系統 | strangulation |
| Cherry-pick | 只挑特定 commit 合併到目的分支 | branch-for-release |
| Merge Meister | 負責把 trunk 的修正 cherry-pick 到 release 分支的人/角色 | branch-for-release |
| Shared nothing / microcosm | 開發機自成一套,不依賴共享服務,斷網也能跑全部測試 | observed-habits |
| Thin vertical slice / INVEST | 小而跨層、可獨立完成的需求切法 | observed-habits、deciding-factors |
| Test Impact Analysis(TIA) | 依覆蓋率地圖,只跑受變更影響的測試 | fast-green-builds |
| Fix forward / roll forward | 在 trunk 修 bug 再發新版,不開 release 分支 | release-from-trunk |

## 3. 方法論核心

1. **單一共享主線**,所有人每天多次整合進去;CI 的核心要求是每人至少每 24 小時提交到 trunk。 `[首頁]`
2. **永遠不弄壞 build、永遠保持可發布。** `[5-min-overview]`
3. **提交要小而頻繁**,每個 commit 都應能獨立上線,即使故事還沒做完。 `[committing-straight][short-lived]`
4. **本機先跑完整 build 再推**,不能把 CI 當拐杖。 `[styles][fast-green-builds]`
5. **未完成的大工作用手段藏起來,不用分支**:feature flag、Branch by Abstraction、Strangulation。 `[首頁][branch-by-abstraction]`
6. **釋出方式二選一**:高頻率→直接從 trunk 發;低頻率→發布前才切 release 分支。 `[5-min-overview]`
7. **審查要快**,等待審查的時間算進分支壽命。 `[short-lived][continuous-review]`
8. 層級關係:TBD 是基礎 → CI → CD → Lean 實驗。 `[context]`

## 4. 三種風格與適用範圍  `[styles]`

| 風格 | 站上給的活躍提交者範圍 | 重點 |
|---|---|---|
| 直接提交 trunk | 1–100(但站上他處說現代無 AI 的團隊多在 16 人以下) | 吞吐最高,但極依賴本機 build 與快速 revert |
| 短命功能分支(PR) | 2–1000(short-lived 頁寫約 15 人以上較適合) | trunk 幾乎不壞;代價是要說服同事快速審查 |
| 耦合的 patch review | 2–40,000 | Google 的 Mondrian 路線;審查系統不在 VCS 內 |

注意:站上自己承認「多少人算小團隊」有爭議(有人說 100,有人說 10 甚至 2)。
站上在 2026 版新增觀點:有 coding agent 時,應該算「同時寫入者」與「尖峰提交頻率」,不是人頭。 `[styles][deciding-factors]`

選型的取捨清單(節錄):build 是否要為每次提交全建、VCS 推拉瓶頸、build 時間 vs 提交頻率、是否把 CI 當拐杖、
能否接受提交後才收到審查意見、是否有 merge queue 等。 `[styles]`

## 5. 執行步驟

### 5.1 每日開發迴圈  `[5-min-overview][observed-habits][committing-straight]`

1. 從 trunk clone / pull(一天多次,即 Chasing HEAD)。
2. 取小任務(數小時完成為佳,超過幾天就會有人想開分支)。
3. 實作並在**本機**跑完整 build(編譯、單元、整合測試)。
4. 再同步一次 trunk,必要時重 build(處理 race to push)。
5. 依團隊風格:直接 push,或開短命分支發 PR。
6. 審查(結對、PR、或 AI 主導審查)。
7. 合併、刪除分支。CI 在 trunk 上再驗一次。
8. 壞了:快速 revert(人工或機器人),不要邊等邊修。

### 5.2 短命功能分支規則  `[short-lived-feature-branches]`

- 壽命:約兩天內。人數:一人或一對。每人同時只該有一條。
- 允許的合併方向:trunk 可合進分支(追上 HEAD);分支只在收尾時合回 trunk。
- 禁止(違約):對 trunk 做不能獨立上線的中途合併;合進別人分支;合進 release 分支;
  從別人機器直接拉分支;別人加入你的分支(結對夥伴除外)。
- 陷阱:「一個故事一個 PR」。應拆成多個小 PR(重構一個、功能一個…)。

### 5.3 發布  `[branch-for-release][release-from-trunk]`

- 低頻率:發布前幾天才切 release 分支,之後不再有一般開發。可從舊 commit 或事後(retroactive)切分支。
- 修 bug:先在 trunk 重現並修+測試,再 **cherry-pick 到 release 分支**,等 release 分支 CI 驗證。方向只能 trunk → release。
- 發布後 release 分支不合回 trunk,過一陣子刪除(Git 刪前先打 tag)。
- 高頻率:直接從 trunk(可用 tag)發布,fix forward。每日一次以下可選擇性開分支撿 bug fix。

### 5.4 大改動的做法

- **Branch by Abstraction** `[branch-by-abstraction]`:① 在舊碼外加抽象層並提交;② 在抽象層後寫新實作並關閉;
  ③ 一個小 commit 切換開關;④ 移除舊實作;⑤ 移除抽象層與開關。兩條守則:不拖慢他人、任何 commit 不得破壞上線能力。
  案例:ThoughtWorks Go 把 iBatis 換成 Hibernate。
- **Feature flag** `[feature-flags]`:能避免 if/else 就用抽象+設定注入;CI 要涵蓋合理的旗標組合(單元測試後扇出);
  執行期可切換的旗標需持久化。
- **Strangulation** `[strangulation]`:用前置代理依 URL 逐步導流到新系統,適合不同語言/不同進程;BbA 適合同語言。

## 6. 規則清單(站上明示的)

1. 不弄壞 build,隨時可發布。 `[5-min-overview]`
2. 本機先跑與 CI 相同的完整 build,通過才推。 `[committing-straight][styles]`
3. 短命分支:≤約 2 天、1 人(或 1 對/1 人加其 agent)。 `[short-lived]`
4. 沒有 code freeze;每天節奏一致;也不要「unmerge」把 commit 撤出 trunk。 `[observed-habits][youre-doing-it-wrong]`
5. 未完成的程式碼藏在 feature flag 後面。 `[youre-doing-it-wrong]`
6. 修 bug 先在 trunk,再 cherry-pick 到 release;不回頭把 release 修正合回 trunk(除非無法在 trunk 重現)。 `[branch-for-release]`
7. 只從 trunk 往 release 分支 cherry-pick;不在 release 分支之間合併;不長期保留單一 release 分支。 `[youre-doing-it-wrong]`
8. 審查不得以「這是我的模組」為由拒絕,拒絕要有公開客觀理由。 `[continuous-review]`
9. PR 審查時間:數分鐘最好,數十分鐘可接受,超過一兩小時就傷害週期時間。 `[continuous-review]`
10. 不要把 CI 當個人 build,反覆 push 看紅綠是壞習慣。 `[fast-green-builds][youre-doing-it-wrong]`
11. 任何人可讀整個 trunk;寫入權限可細分。 `[deciding-factors]`
12. 超過三個人的團隊需要 CI 伺服器。 `[continuous-integration]`
13. 旗標要定期清理(建議發布後約一個月,標註「review for delete」日期)。 `[feature-flags]`

## 7. 適用條件(站上提到的前提)

- 基礎:可靠的開發基礎設施、版本控制足夠快(無變更時 pull < 3 秒,有變更 < 15 秒)。 `[context][deciding-factors]`
- 故事要小(數小時到兩天);站上建議用 INVEST 與薄垂直切片。 `[deciding-factors][observed-habits]`
- build 要快(約 1 分鐘算快,10 分鐘以上算慢)。 `[committing-straight]`
- 敏捷/短迭代;**瀑布式與四週以上且無法改變的迭代不適合**。 `[deciding-factors]`
- 倉庫大小:Git/Mercurial 歷史建議不超過 1GB(不含 LFS);大型二進位檔需 LFS 或 Perforce 類工具。 `[deciding-factors]`
- 團隊文化:共同程式碼所有權、快速審查、客觀規則。 `[observed-habits][continuous-review]`
- 資料庫 schema 需要能在版本控制下演進(站上只指向書籍,未展開)。 `[deciding-factors]`
- 康威定律:組織結構若不適合單一 repo,可能走向微服務。 `[deciding-factors]`

## 8. 例外與限制(站上承認的)

- 「直接提交 trunk」在高提交率、build 慢時會崩潰:站上舉例 build 5 分鐘但每 10 秒一個提交就是地獄。 `[styles]`
- Branch by Abstraction **不是萬靈丹**:若要長期支援舊 API、舊版本、客戶自己決定升級時間,就不適合。 `[branch-by-abstraction]`
- 無法在 trunk 重現的 bug,可例外先在 release 分支修再合回,但風險上升。 `[branch-for-release]`
- 高頻率 CD 團隊可完全不開 release 分支。 `[branch-for-release]`
- 企業治理可能要求具名人工核准,因此保留 PR,即使整合本可直接進行。 `[styles][short-lived]`
- 平行開發連續版本(concurrent development of consecutive releases)站上認為「連續開發連續版本」才是最佳;
  但若被迫平行,TBD+旗標+抽象能做為避險(航空公司 2012 案例)。 `[concurrent-development...]`
- CI 伺服器不等於 TBD:多分支團隊也能裝 CI,站上稱之為「Daemonic CI」的自我感覺良好。 `[continuous-integration]`
- 站上自述若 Git 的 pull-before-push 在高頻下失效,要靠 merge queue。 `[deciding-factors]`

## 9. 與其他模型的比較  `[alternative-branching-models][youre-doing-it-wrong]`

- **GitHub flow**:很接近 PR 式 TBD;關鍵差別是「從哪裡發布」(GitHub flow 在合回前從分支發布)。
  PR 放數天就等於長命分支。
- **GitFlow**:站上稱與 TBD **不相容**(多組開發者在多條長命分支)。
- **Mainline**(ClearCase 時代):與 TBD 正相反;大合併、凍結、不易隨時可發布。
- **多 trunk 單 repo**:不建議,建議單 trunk 多模組 + 有向圖建置系統(Bazel/Buck)。

## 10. 加分:AI coding agent 的新增段落(2026 版)

站上新增大量 agent 相關論述,這是最近才加的,時間較新、論述性較強,要和經典規則區分:

- agent 改變規模不改變原則;每個 worktree 是一條短命分支,適用同樣規則。 `[short-lived][5-min-overview]`
- 提交競爭變激烈,需要 commit/merge queue。 `[committing-straight]`
- 審查要由獨立的 AI 審查者主導,人類在更上層(on the loop)。 `[continuous-review]`
- agent 不要「寫完實作再補測試」,測試要看過紅燈才有意義;可用 mutation testing。 `[fast-green-builds]`

## 11. 站上的主要外部出處(轉引)

- Martin Fowler / Matt Foemmel,〈Continuous Integration〉2000、2006。
- Laura Wingerd & Christopher Seiwald,Perforce〈High-Level SCM Best Practices〉1998(branch 時機)。
- Rachel Potvin,@Scale 2015:Google 單一 monorepo 與 trunk。
- Jez Humble & Dave Farley,《Continuous Delivery》2010;《DevOps Handbook》2016。(完整清單在 publications 頁,我未讀內文)
- Vincent Driessen,GitFlow 原文 2010(被站上拿來對照)。

## 12. 我(整理者)的保留意見,供討論

- 站上是倡議型網站,作者立場鮮明(例如「Mainline 公司會凋零」屬主張而非證據)。
- 團隊規模門檻(15、16、100、1000…)是經驗值,站上自己說有爭議。
- 2026 年的 AI 段落包含對 Anthropic 提交頻率的推測引用,屬轉述性質,不宜當硬數據。

# Conventional Commits 1.0.0 規則與來源

資料取得:2026-11-08;規格原始檔複核 2026-11-09。
標記:【已核對原文】與規格原始 Markdown 比對過;【摘要】只讀過工具摘要;【推論】推理,來源未明寫。

## 來源
| 代號 | 內容 | 網址 |
|---|---|---|
| S1 | 規格本文 v1.0.0 | https://www.conventionalcommits.org/en/v1.0.0/ |
| S2 | About(淵源、工具、採用專案) | https://www.conventionalcommits.org/en/about/ |
| S3 | SemVer 2.0.0 | https://semver.org/ |
| S4 | Angular 提交訊息指引 | https://github.com/angular/angular/blob/22b96b9/CONTRIBUTING.md#-commit-message-guidelines |
| S5 | commitlint config-conventional | https://github.com/conventional-changelog/commitlint/tree/master/%40commitlint/config-conventional |
| S6 | RFC 2119 | https://www.ietf.org/rfc/rfc2119.txt |
| S7 | git trailer 格式 | https://git-scm.com/docs/git-interpret-trailers |
| S8 | 規格原始 Markdown | https://raw.githubusercontent.com/conventional-commits/conventionalcommits.org/master/content/v1.0.0/index.md |

規格授權 CC BY 3.0:https://creativecommons.org/licenses/by/3.0/ 。
S1 章節:Summary / Examples(7 例)/ Specification(16 條)/ Why Use / FAQ(10 題)。

## 規格 16 條(S1 Specification,編號與內容【已核對原文】,措辭為重述)
1. 必須以 type 開頭,後接可選 scope、可選 `!`、必要的冒號與空格。
2. 新功能必須用 `feat`。
3. 修 bug 必須用 `fix`。
4. scope 可選,括號內的名詞,指程式碼某區塊。
5. 描述必須緊接在冒號空格之後。
6. body 可選,在描述後空一行開始。
7. body 自由格式,可多段。
8. footer 可選,在 body 後空一行;格式為 token + 分隔符(`: ` 或 ` #`)+ value。
9. footer token 以 `-` 取代空格(`BREAKING CHANGE` 例外)。
10. footer 值可含空格與換行,遇下一個合法 token 加分隔符即結束。
11. 破壞性變更必須標在前綴或 footer。
12. footer 形式必須是大寫 `BREAKING CHANGE: 描述`。
13. 前綴形式用 `!`(冒號前);使用 `!` 時 footer 可省略,由描述說明。
14. 可使用 `feat`、`fix` 以外的 type。
15. 各元素不分大小寫,唯 `BREAKING CHANGE` 必須大寫。
16. footer token 的 `BREAKING-CHANGE` 視同 `BREAKING CHANGE`。

原文短引文:規則 1「Commits MUST be prefixed with a type.」;規則 2「The type `feat` MUST be used when a commit adds a new feature.」;規則 3「The type `fix` MUST be used when a commit represents a bug fix.」;規則 5「A description MUST immediately follow the colon and space.」;規則 11「Breaking changes MUST be indicated in the type/scope prefix of a commit, or as an entry in the footer.」

## 版本對應(S1 Summary / FAQ、S3)
- `fix` → PATCH;`feat` → MINOR;任何 type 帶 BREAKING CHANGE → MAJOR。
- 其他 type 沒有隱含的版本效果【S1】。
- 同一次發佈取最高級別;升高位時低位歸零(SemVer 規則 7、8)【S3 摘要】。
- 0.y.z 為初期開發,API 不穩定(SemVer 規則 4)【S3 摘要】;Conventional Commits 在 0.x 如何對應,規格未規定,取決於工具【推論】。

## FAQ 例外(S1 FAQ,【摘要】)
| 情況 | 規格說法 |
|---|---|
| 初期開發 | 當成已發佈,因為別人可能已在用 |
| 大小寫 | 可任意,但要一致 |
| 符合多種 type | 盡量拆成多個 commit |
| 錯 type(合法 type) | 發佈前 `git rebase -i`;發佈後依工具與流程 |
| 錯 type(不在規格內,如 `feet`) | 不致命,工具會漏掉該 commit |
| 貢獻者不照規格 | 可用 squash 工作流,由維護者合併時整理訊息 |
| revert | 規格未定義,留給工具作者;建議 `revert` type 加 footer 引用 SHA |
| 擴充規格 | 鼓勵,擴充用 SemVer 版本化 |

## 慣例層(不在規格內)
- 常見 type:`build chore ci docs feat fix perf refactor revert style test`【S4、S5】。
- commitlint 預設【S5 摘要】:type 必須小寫且在清單內;subject 不得為 sentence/start/pascal/upper case;subject 不以句點結尾;header ≤ 100 字元;body、footer 前要有空行(警告);body、footer 每行 ≤ 100(含 URL 的行例外)。
- Angular【S4 摘要】:祈使句現在式、首字小寫、無句點。

## 尚待查證
- `Breaking-Change:`(非全大寫)是否算破壞性標記:規則 15 與 16 的文字有解讀空間,我傾向「不算」【推論】,以所用解析工具的行為為準。
- 已發佈版本不可修改(SemVer 規則 3):憑記憶,未重讀原文。
- 各工具(semantic-release 等)在 0.x、revert、自訂 type 的實際行為:未驗證。

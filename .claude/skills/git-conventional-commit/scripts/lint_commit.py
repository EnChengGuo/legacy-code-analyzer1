# /// script
# requires-python = ">=3.9"
# dependencies = []
# ///
"""檢查 commit 訊息是否符合 Conventional Commits 1.0.0,並推算版本影響。

用法:
    uv run lint_commit.py <訊息檔>       # 或 python lint_commit.py <訊息檔>
    echo "feat: add x" | python lint_commit.py -
    python lint_commit.py msg.txt --json

退出碼:0 通過(可能有慣例提醒)、1 有違反規格的錯誤、2 用法錯誤(讀不到檔案等)。
只用標準庫。只檢查結構,不判斷 type 選得對不對。
"""
import argparse
import json
import re
import sys

COMMON_TYPES = {"build", "chore", "ci", "docs", "feat", "fix", "perf",
                "refactor", "revert", "style", "test"}

HEADER_RE = re.compile(
    r"^(?P<type>[A-Za-z][A-Za-z0-9_-]*)"
    r"(?:\((?P<scope>[^()\r\n]+)\))?"
    r"(?P<bang>!)?"
    r": (?P<desc>\S.*)$"
)
FOOTER_RE = re.compile(r"^(?P<token>BREAKING CHANGE|[A-Za-z][A-Za-z0-9-]*)(?P<sep>: | #)(?P<value>.*)$")
BREAKING_TOKENS = {"BREAKING CHANGE", "BREAKING-CHANGE"}


def diagnose_header(h):
    """標頭不合規時,盡量說出原因。"""
    if not h.strip():
        return "標頭是空的(規則 1)"
    if re.match(r"^[A-Za-z][A-Za-z0-9_-]*\s+\(", h):
        return "type 與 scope 括號之間有空格:要緊貼,如 `fix(parser):`(規則 1、4)"
    if re.match(r"^[A-Za-z][A-Za-z0-9_-]*(\([^()]*\))?!?\s+:", h):
        return "冒號前有空格:type/scope/! 與冒號之間要緊貼(規則 1)"
    if re.match(r"^[A-Za-z][A-Za-z0-9_-]*(\([^()]*\))?!?:\S", h):
        return "冒號後少了空格:必須是「冒號加一個空格」(規則 1、5)"
    if re.match(r"^[A-Za-z][A-Za-z0-9_-]*(\([^()]*\))?!?:\s*$", h) or re.match(r"^.*: +$", h):
        return "描述是空的:冒號空格後必須緊接描述(規則 5)"
    if re.match(r"^[A-Za-z][A-Za-z0-9_-]*(\([^()]*\))?!?: {2,}\S", h):
        return "冒號後有多個空格:應為單一空格(規則 5)"
    if ":" not in h:
        return "缺少 `type: 描述` 結構:必須以 type 開頭並接冒號空格(規則 1)"
    if re.match(r"^[^A-Za-z]", h):
        return "標頭必須以 type(英文字母開頭)開始(規則 1)"
    return "標頭格式不符:應為 type(scope)!: 描述(scope 為括號內名詞,不可含括號)(規則 1、4)"


def lint(message, strip_comments=False):
    errors, warnings = [], []
    text = message.replace("\r\n", "\n").replace("\r", "\n")
    lines = text.split("\n")
    if strip_comments:
        lines = [l for l in lines if not l.startswith("#")]
    while lines and lines[-1].strip() == "":
        lines.pop()
    result = {"errors": errors, "warnings": warnings, "type": None, "scope": None,
              "breaking": False, "bump": "none", "footers": []}
    if not lines:
        errors.append("訊息是空的")
        return result

    header = lines[0]
    m = HEADER_RE.match(header)
    if not m:
        errors.append(f"標頭不合規:{diagnose_header(header)}")
    else:
        result["type"] = m["type"]
        result["scope"] = m["scope"]
        bang = bool(m["bang"])
        t = m["type"]
        desc = m["desc"]

        # 慣例層
        if t != t.lower():
            warnings.append(f"type `{t}` 不是小寫:規格不分大小寫,但 commitlint 預設要求小寫(規則 15)")
        if t.lower() not in COMMON_TYPES:
            warnings.append(f"type `{t}` 不在常見清單 {sorted(COMMON_TYPES)}:規格允許(規則 14),"
                            "但工具可能略過它,不會升版也不會進 changelog;若是打錯字請改正")
        if m["scope"] and m["scope"] != m["scope"].lower():
            warnings.append(f"scope `{m['scope']}` 含大寫:規格未禁止,commitlint 預設要求小寫")
        if m["scope"] and " " in m["scope"]:
            warnings.append(f"scope `{m['scope']}` 含空格:規格未明文禁止,慣例上用單一名詞")
        if desc.rstrip().endswith("."):
            warnings.append("描述以句點結尾:慣例不加句點")
        if desc[0].isupper():
            warnings.append("描述首字大寫:Angular / commitlint 慣例為小寫開頭")
        if len(header) > 100:
            warnings.append(f"標頭 {len(header)} 字元,超過常見上限 100")
        if desc.strip().lower() in {"update", "fix", "changes", "wip", "fix bug", "misc", "updates"}:
            warnings.append(f"描述 `{desc}` 沒有資訊量:請說明這次變更的結果")
        result["_bang"] = bang

    # body / footer
    rest = lines[1:]
    if rest:
        if rest[0].strip() != "":
            errors.append("標頭下一行不是空行:body 必須在描述後空一行才開始(規則 6)")
        paras, cur = [], []
        for l in rest[1:] if rest[0].strip() == "" else rest:
            if l.strip() == "":
                if cur:
                    paras.append(cur)
                    cur = []
            else:
                cur.append(l)
        if cur:
            paras.append(cur)

        # 最後一段若首行是 footer 格式,視為 footer 區塊
        footers = []
        if paras and FOOTER_RE.match(paras[-1][0]):
            for l in paras[-1]:
                fm = FOOTER_RE.match(l)
                if fm:
                    footers.append([fm["token"], fm["sep"], fm["value"]])
                elif footers:
                    footers[-1][2] += "\n" + l  # 多行值(規則 10)
        result["footers"] = [{"token": f[0], "value": f[2]} for f in footers]

        for tok, sep, val in footers:
            if tok in BREAKING_TOKENS:
                result["breaking"] = True
                if val.strip().lower() in {"none", "n/a", "no", "無", "沒有"}:
                    warnings.append(f"`{tok}: {val.strip()}` 表示沒有破壞性,但工具只看 token 會誤升 MAJOR;請移除此 footer")

        # 常見誤寫:小寫 / 空格寫法的 breaking change
        for l in rest:
            if re.match(r"(?i)^breaking[ -]change\s*:", l) and not re.match(r"^BREAKING[ -]CHANGE: ", l):
                warnings.append(f"`{l.split(':')[0]}:` 不是大寫 `BREAKING CHANGE`,工具不會當成破壞性標記(規則 12、15);"
                                "若確實是破壞性變更,請改成大寫或在標頭加 `!`")
            if paras and l in paras[-1] and re.match(r"^[A-Z][a-z]+ [a-z]+: \S", l):
                warnings.append(f"`{l.split(':')[0]}:` 若是 footer,token 含空格不合規,應以 `-` 取代空格,"
                                "如 `Reviewed-by:`(規則 9);若只是正文可忽略")

    if result.get("_bang"):
        result["breaking"] = True
    result.pop("_bang", None)

    # 版本影響
    if result["type"]:
        if result["breaking"]:
            result["bump"] = "major"
        elif result["type"].lower() == "feat":
            result["bump"] = "minor"
        elif result["type"].lower() == "fix":
            result["bump"] = "patch"
    return result


def main():
    ap = argparse.ArgumentParser(description="檢查 commit 訊息是否符合 Conventional Commits 1.0.0")
    ap.add_argument("file", help="訊息檔路徑,或 - 表示從 stdin 讀取")
    ap.add_argument("--json", action="store_true", help="輸出 JSON")
    ap.add_argument("--strip-comments", action="store_true",
                    help="略過以 # 開頭的行(用於 git 的 COMMIT_EDITMSG;注意 body 中以 # 開頭的 issue 參照也會被略過)")
    args = ap.parse_args()
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except (AttributeError, ValueError):
            pass
    try:
        if args.file == "-":
            data = sys.stdin.read()
        else:
            with open(args.file, encoding="utf-8-sig") as f:
                data = f.read()
    except OSError as e:
        print(f"讀取失敗:{e}", file=sys.stderr)
        return 2

    r = lint(data, args.strip_comments)
    if args.json:
        print(json.dumps(r, ensure_ascii=False, indent=2))
    else:
        status = "通過" if not r["errors"] else "不合規"
        print(f"結果:{status}")
        for e in r["errors"]:
            print(f"  [規格錯誤] {e}")
        for w in r["warnings"]:
            print(f"  [慣例提醒] {w}")
        if r["type"]:
            scope = f"({r['scope']})" if r["scope"] else ""
            print(f"  type={r['type']}{scope}  breaking={r['breaking']}  bump={r['bump']}")
    return 1 if r["errors"] else 0


if __name__ == "__main__":
    sys.exit(main())

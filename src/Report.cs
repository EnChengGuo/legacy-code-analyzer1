using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeAtlas
{
    /// <summary>給 Copilot 的 AI 上下文(只給簽章與位置,內文需要時再讀)與總覽儀表板。</summary>
    public static class Report
    {
        static readonly SymbolDisplayFormat Sig = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers
                | SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters
                | SymbolDisplayMemberOptions.IncludeExplicitInterface,
            parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        static string Range(ISymbol s)
        {
            var r = s.DeclaringSyntaxReferences.FirstOrDefault();
            if (r == null) return "";
            var ls = r.SyntaxTree.GetLineSpan(r.Span);
            return Path.GetFileName(ls.Path) + ":" + (ls.StartLinePosition.Line + 1) + "-" + (ls.EndLinePosition.Line + 1);
        }

        internal static string Summary(ISymbol s)
        {
            var xml = s.GetDocumentationCommentXml();
            if (string.IsNullOrWhiteSpace(xml)) return "";
            try
            {
                var el = System.Xml.Linq.XDocument.Parse("<r>" + xml + "</r>").Descendants("summary").FirstOrDefault();
                return el == null ? "" : string.Join(" ", el.Value.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }
            catch { return ""; }
        }

        /// <summary>摘要式上下文:簽章、位置、度量、關係;不含方法內文。</summary>
        public static string Context(Analyzer an, SymbolInfoItem target, int maxMembers)
        {
            var sb = new StringBuilder();
            var s = target.Symbol;
            var mm = Metrics.Members(an).ToDictionary(m => m.Item.Key);
            sb.AppendLine("# " + target.Kind + " " + target.Display);
            sb.AppendLine("- 位置:" + Range(s) + "(專案 " + target.Project + ")");
            sb.AppendLine("- 簽章:`" + s.ToDisplayString(Sig) + "`");
            var doc = Summary(s);
            if (doc != "") sb.AppendLine("- 說明:" + doc);
            MemberMetric m;
            if (mm.TryGetValue(target.Key, out m))
                sb.AppendLine("- 度量:CC=" + m.CC + ",巢狀=" + m.MaxNesting + ",行數=" + m.Loc + ",MI=" + m.MI.ToString("0"));

            var t = s as INamedTypeSymbol;
            if (t != null)
            {
                if (t.BaseType != null && t.BaseType.SpecialType != SpecialType.System_Object)
                    sb.AppendLine("- 繼承:" + t.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                if (t.Interfaces.Length > 0)
                    sb.AppendLine("- 實作介面:" + string.Join("、", t.Interfaces.Select(i => i.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))));
                var members = t.GetMembers().Where(x => !x.IsImplicitlyDeclared && !(x is IMethodSymbol
                    && ((IMethodSymbol)x).AssociatedSymbol != null)).ToList();
                sb.AppendLine();
                sb.AppendLine("## 成員(共 " + members.Count + " 個,簽章 + 位置;內文請用 body 指令按需讀取)");
                foreach (var x in members.Take(maxMembers))
                {
                    MemberMetric xm;
                    var cc = mm.TryGetValue(Analyzer.Key(x), out xm) ? "  CC=" + xm.CC : "";
                    sb.AppendLine("- `" + x.ToDisplayString(Sig) + "`  " + Range(x) + cc);
                }
                if (members.Count > maxMembers)
                    sb.AppendLine("- …另有 " + (members.Count - maxMembers) + " 個成員未列出;請指定「類別.成員」取得個別上下文。");
            }

            Action<string, List<Ref>, bool> rel = (title, list, incoming) =>
            {
                var groups = list.GroupBy(r => incoming ? r.EnclosingDisplay : r.TargetKey).OrderByDescending(g => g.Count()).ToList();
                if (groups.Count == 0) return;
                sb.AppendLine();
                sb.AppendLine("## " + title + "(" + groups.Count + ")");
                foreach (var g in groups.Take(10))
                {
                    SymbolInfoItem it;
                    var name = an.Declared().TryGetValue(g.Key, out it) ? it.Display : (incoming ? g.Key : g.First().TargetKey);
                    sb.AppendLine("- " + name + "(" + g.Count() + " 處)");
                }
                if (groups.Count > 10) sb.AppendLine("- …另 " + (groups.Count - 10) + " 項省略");
            };
            if (t == null)
            {
                rel("被誰使用", an.IncomingCalls(target), true);
                rel("使用了誰", an.OutgoingCalls(target), false);
            }
            else
            {
                var tm = Metrics.Types(an, mm.Values.ToList()).First(x => x.Item.Key == target.Key);
                sb.AppendLine();
                sb.AppendLine("## 類別度量:WMC=" + tm.WMC + ",Ca=" + tm.Ca + ",Ce=" + tm.Ce + ",行數=" + tm.Loc);
            }
            var text = sb.ToString();
            sb.AppendLine();
            sb.AppendLine("> 這份摘要約 " + text.Length + " 個字元,粗估 " + (text.Length / 2) + " 個 token 以內。方法內文未包含。");
            return sb.ToString();
        }

        /// <summary>只讀某個成員(或整個類別)的原始碼片段,附行號。</summary>
        public static string Body(SymbolInfoItem target, int fromLine = 0, int toLine = 0)
        {
            var sb = new StringBuilder();
            foreach (var r in target.Symbol.DeclaringSyntaxReferences)
            {
                var text = r.SyntaxTree.GetText();
                var ls = r.SyntaxTree.GetLineSpan(r.Span);
                int a = ls.StartLinePosition.Line, b = ls.EndLinePosition.Line;
                // 指定行範圍:只讀函式裡的一段(行號用檔案的絕對行號,超出範圍會被裁到函式內)
                if (fromLine > 0) a = Math.Max(a, fromLine - 1);
                if (toLine > 0) b = Math.Min(b, toLine - 1);
                if (a > b) continue;
                sb.AppendLine("// " + ls.Path + " 第 " + (a + 1) + "–" + (b + 1) + " 行");
                for (int i = a; i <= b; i++) sb.AppendLine((i + 1).ToString().PadLeft(5) + "  " + text.Lines[i]);
            }
            return sb.ToString();
        }

        // ---------- 儀表板 ----------
        static string H(object o) { return WebUtility.HtmlEncode(Convert.ToString(o)); }

        public static string Dashboard(Analyzer an, bool served = false)
        {
            var members = Metrics.Members(an);
            var types = Metrics.Types(an, members);
            var dead = Metrics.Dead(an);
            var cycles = Metrics.Cycles(an);
            var diags = an.Diagnostics();
            int files = an.Projects.Sum(p => p.SourceFiles.Count);

            var sb = new StringBuilder();
            sb.Append(@"<!doctype html><html lang='zh-Hant'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>CodeAtlas 總覽</title><style>
:root{--bg:#f6f7f9;--fg:#1f2430;--card:#fff;--mut:#6b7280;--line:#e5e7eb;--acc:#2563eb;--bad:#dc2626;--warn:#d97706;--ok:#16a34a}
@media(prefers-color-scheme:dark){:root{--bg:#14171c;--fg:#e5e7eb;--card:#1d2128;--mut:#9ca3af;--line:#2d333b;--acc:#60a5fa}}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.6 'Microsoft JhengHei',system-ui,sans-serif}
header{position:sticky;top:0;background:var(--card);border-bottom:1px solid var(--line);padding:10px 20px;display:flex;gap:16px;align-items:center}
header a{color:var(--acc);text-decoration:none;font-weight:600}main{max-width:1100px;margin:0 auto;padding:16px 20px}
.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;margin-bottom:20px}
.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:12px}.card b{display:block;font-size:24px}.card span{color:var(--mut)}
h2{margin:28px 0 8px;font-size:16px}table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:8px}
th,td{text-align:left;padding:6px 10px;border-bottom:1px solid var(--line);vertical-align:top}th{color:var(--mut);font-weight:600}
.num{text-align:right;font-variant-numeric:tabular-nums}.bad{color:var(--bad)}.warn{color:var(--warn)}.ok{color:var(--ok)}.mut{color:var(--mut)}
</style></head><body id='top'><header>" + (served ? "<a href='/'>🏠 總覽</a><a href='/classes'>類別圖與關係圖</a><a href='/flow'>函式流程</a><a href='/canvas'>流程畫布</a><a href='/god'>God Class 解剖</a><a href='/clones'>重複程式碼</a>" : "<a href='#top'>🏠 總覽</a>") + @"<a href='#metrics'>品質度量</a><a href='#dead'>死碼</a><a href='#cycles'>循環相依</a><a href='#diag'>編譯診斷</a></header><main>");
            sb.Append("<h1>CodeAtlas 總覽</h1><div class='cards'>");
            Action<string, object> card = (l, v) => sb.Append("<div class='card'><b>" + H(v) + "</b><span>" + H(l) + "</span></div>");
            card("專案", an.Projects.Count); card("原始檔", files); card("型別", types.Count); card("成員", an.Declared().Count - types.Count);
            card("總行數(成員內)", members.Sum(m => m.Loc)); card("高可信度死碼", dead.Count(d => d.Confidence == "高"));
            card("循環相依群組", cycles.Count); card("編譯診斷", diags.Count);
            sb.Append("</div>");

            sb.Append("<h2 id='metrics'>最複雜的方法(CC 由高到低,前 15)</h2><table><tr><th>成員</th><th class='num'>CC</th><th class='num'>巢狀</th><th class='num'>行數</th><th class='num'>MI</th></tr>");
            foreach (var m in members.OrderByDescending(x => x.CC).Take(15))
                sb.Append("<tr><td>" + H(m.Item.Display) + "</td><td class='num " + (m.CC > 20 ? "bad" : m.CC > 10 ? "warn" : "") + "'>" + m.CC + "</td><td class='num'>" + m.MaxNesting
                    + "</td><td class='num'>" + m.Loc + "</td><td class='num " + (m.MI < 20 ? "bad" : m.MI < 40 ? "warn" : "ok") + "'>" + m.MI.ToString("0") + "</td></tr>");
            sb.Append("</table>");

            sb.Append("<h2>最大的類別(WMC 由高到低,前 15)</h2><table><tr><th>類別</th><th class='num'>成員</th><th class='num'>WMC</th><th class='num'>Ca</th><th class='num'>Ce</th><th class='num'>不穩定度</th></tr>");
            foreach (var t in types.OrderByDescending(x => x.WMC).Take(15))
                sb.Append("<tr><td>" + H(t.Item.Display) + "</td><td class='num'>" + t.Members + "</td><td class='num'>" + t.WMC + "</td><td class='num'>" + t.Ca
                    + "</td><td class='num'>" + t.Ce + "</td><td class='num'>" + t.Instability.ToString("0.00") + "</td></tr>");
            sb.Append("</table><p class='mut'>CC=循環複雜度;MI=維護性指數(近似值);WMC=類別內方法 CC 總和;Ca=被多少其他類別使用;Ce=使用了多少其他類別;不穩定度=Ce/(Ca+Ce)。</p>");

            sb.Append("<h2 id='dead'>死碼(" + dead.Count + ")</h2><table><tr><th>可信度</th><th>成員</th><th>原因</th></tr>");
            foreach (var d in dead.Take(100))
                sb.Append("<tr><td class='" + (d.Confidence == "高" ? "bad" : d.Confidence == "中" ? "warn" : "mut") + "'>" + d.Confidence + "</td><td>" + H(d.Item.Kind + " " + d.Item.Display) + "</td><td>" + H(d.Reason) + "</td></tr>");
            sb.Append("</table>");

            sb.Append("<h2 id='cycles'>循環相依(" + cycles.Count + ")</h2>");
            if (cycles.Count == 0) sb.Append("<p class='ok'>沒有發現循環相依。</p>");
            foreach (var c in cycles.Take(30))
                sb.Append("<p><b>" + H(c.Level) + "</b>(" + c.Nodes.Count + " 個):" + H(string.Join("、", c.Nodes.Take(12))) + (c.Nodes.Count > 12 ? " …" : "") + "</p>");

            sb.Append("<h2 id='diag'>編譯診斷(" + diags.Count + ")</h2>");
            if (diags.Count == 0) sb.Append("<p class='ok'>沒有警告或錯誤。</p>");
            foreach (var d in diags.Take(100)) sb.Append("<div>" + H(d) + "</div>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CodeAtlas
{
    static class Program
    {
        const string Usage =
@"用法:CodeAtlas <指令> <方案.sln|專案.csproj> [參數]
  symbols                列出所有型別
  find   <名稱>          尋找符號(例:Order、Order.ApplyDiscount)
  refs   <名稱>          找參考,區分 讀 / 寫 / 讀寫 / 呼叫
  calls  <名稱> [層數]   呼叫階層(誰呼叫它、它呼叫誰),預設 2 層
  impact <名稱> [層數]   影響分析:改了它會牽動哪些成員,預設 5 層
  flow   <方法> [展開層數] 函式流程:循序圖與活動圖(Mermaid + PlantUML)
  (任何指令都可加)  --source DLL名稱=原始碼位置   用原始碼取代某個 DLL;也可在方案旁放 codeatlas.sources
  clones [標記數] [最少行數]  重複程式碼偵測(預設 40 標記、6 行)
  debtors <類別>         冤親債主:沒寫註解的成員,最後編輯者與歷代編輯者(需要 git)
  serve [方案] [埠號] [--open]  啟動本機網頁(預設 5173)。不指定方案時,在網頁的「開啟專案」頁輸入或選擇路徑
                         (不加任何參數直接執行 CodeAtlas.exe 也是這樣)
  metrics [N]          品質度量:CC、MI、巢狀、WMC、Ca、Ce(各列前 N 名,預設 15)
  dead                   死碼偵測(高/中/低可信度)
  cycles                 循環相依(專案/命名空間/型別)
  context <名稱> [上限]  給 Copilot 的 AI 上下文(只含簽章與位置,成員最多列上限個)
  body   <名稱> [起行 迄行]  只讀某成員或類別的原始碼片段;可再指定行號範圍,只讀函式裡的一段
  dashboard [輸出.html]  輸出總覽儀表板
  diag                   編譯診斷
  search <文字>          全文搜尋";

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint GetConsoleProcessList(uint[] ids, uint count);

        /// <summary>這個黑色視窗是不是只屬於本程式(從檔案總管雙擊或拖曳開啟)?從命令列執行時為 false。</summary>
        static bool OwnsConsole() { try { return GetConsoleProcessList(new uint[2], 2) == 1; } catch { return false; } }

        static int Main(string[] args)
        {
            // 把 .sln 或 .csproj 直接拖到 exe 上:等同「serve 檔案 --open」
            if (args.Length == 1)
            {
                var a = args[0].Trim('"');
                if (a.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) args = new[] { "serve", a, "--open" };
            }
            if (args.Length == 0)
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine("CodeAtlas — C# 程式碼語意分析工具\n");
                Console.WriteLine("正在啟動,瀏覽器會開啟「開啟專案」頁面,請在那裡輸入或選擇 .sln / .csproj 的路徑。\n");
            }
            int code;
            try { code = Run(args); }
            catch (Exception ex) { Console.OutputEncoding = Encoding.UTF8; Console.WriteLine("[錯誤] " + ex.Message); code = 1; }
            // 從檔案總管開啟時,結束前停住,避免視窗一閃就消失、看不到訊息
            if ((args.Length < 2 || code != 0) && OwnsConsole())
            {
                Console.WriteLine("\n按任意鍵結束…");
                try { Console.ReadKey(true); } catch { }
            }
            return code;
        }

        static int Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // 沒有指定專案:直接啟動網頁,讓使用者在「開啟專案」頁輸入或瀏覽路徑(雙擊 exe 就是這種情況)
            bool noProject = args.Length == 0 || (args[0] == "serve" && (args.Length == 1 || args[1].StartsWith("--") || args[1].All(char.IsDigit)));
            if (noProject)
            {
                Console.OutputEncoding = Encoding.UTF8;
                int p0 = 5173; foreach (var a in args.Skip(1)) { int v; if (int.TryParse(a, out v)) p0 = v; }
                new Server(null).Run(p0, !args.Contains("--no-open"));
                return 0;
            }
            if (args.Length < 2) { Console.WriteLine(Usage); return 2; }
            try
            {
                // --source DLL名稱=原始碼位置(可重複);或在方案旁放 codeatlas.sources,每行一個「DLL名稱=位置」
                var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var rest = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--source" && i + 1 < args.Length)
                    {
                        var kv = args[++i]; var eq = kv.IndexOf('=');
                        if (eq <= 0) { Console.WriteLine("--source 的格式是 DLL名稱=原始碼位置"); return 2; }
                        sources[kv.Substring(0, eq).Trim()] = kv.Substring(eq + 1).Trim();
                    }
                    else rest.Add(args[i]);
                }
                args = rest.ToArray();
                var srcFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[1])) ?? ".", "codeatlas.sources");
                if (System.IO.File.Exists(srcFile))
                {
                    var baseDir = System.IO.Path.GetDirectoryName(srcFile);
                    foreach (var line in System.IO.File.ReadAllLines(srcFile, Encoding.UTF8))
                    {
                        var t = line.Trim(); if (t == "" || t.StartsWith("#")) continue;
                        var eq = t.IndexOf('='); if (eq <= 0) continue;
                        var key = t.Substring(0, eq).Trim();
                        if (!sources.ContainsKey(key)) sources[key] = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, t.Substring(eq + 1).Trim()));
                    }
                }
                var projects = ProjectLoader.Load(args[1], m => Console.WriteLine("[警告] " + m), sources);
                var an = new Analyzer(projects);
                string arg = args.Length > 2 ? args[2] : null;
                int depth;
                switch (args[0])
                {
                    case "symbols":
                        foreach (var p in projects)
                        {
                            Console.WriteLine("專案 " + p.Name + "(" + p.SourceFiles.Count + " 個原始檔)");
                            foreach (var t in p.Compilation.Assembly.GlobalNamespace.GetAllTypes())
                                Console.WriteLine("  " + Analyzer.KindName(t) + " " + t.ToDisplayString()
                                    + "  成員 " + t.GetMembers().Count(m => !m.IsImplicitlyDeclared));
                        }
                        return 0;
                    case "diag":
                        var ds = an.Diagnostics();
                        ds.ForEach(Console.WriteLine);
                        Console.WriteLine("共 " + ds.Count + " 筆");
                        return 0;
                    case "clones":
                        {
                            int win = arg == null ? 40 : int.Parse(arg), ml = args.Length > 3 ? int.Parse(args[3]) : 6;
                            var cl = Clones.Find(an, win, ml, false, false);
                            foreach (var c in cl.Take(50))
                                Console.WriteLine("  " + (c.Exact ? "[完全相同]" : "[名稱或常數不同]") + " " + c.Lines + " 行/" + c.Tokens + " 標記\n    " + System.IO.Path.GetFileName(c.A.File) + ":" + c.A.StartLine + "-" + c.A.EndLine + "  " + c.A.Member
                                    + "\n    " + System.IO.Path.GetFileName(c.B.File) + ":" + c.B.StartLine + "-" + c.B.EndLine + "  " + c.B.Member);
                            Console.WriteLine("共 " + cl.Count + " 組");
                            return 0;
                        }
                    case "emit": // 開發用:把專案編成 DLL(測試 --source 時用)
                        {
                            if (arg == null) { Console.WriteLine("用法:emit <專案.csproj> <輸出.dll>"); return 2; }
                            var res = projects[0].Compilation.Emit(arg);
                            foreach (var d in res.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) Console.WriteLine(d);
                            Console.WriteLine(res.Success ? "已輸出:" + System.IO.Path.GetFullPath(arg) : "編譯失敗");
                            return res.Success ? 0 : 1;
                        }
                    case "serve":
                        {
                            int port = 5173;
                            if (arg != null && !arg.StartsWith("--")) int.TryParse(arg, out port);
                            new Server(an, System.IO.Path.GetFullPath(args[1]), string.Join("\n", sources.Select(kv => kv.Key + "=" + kv.Value))).Run(port, args.Contains("--open"));
                            return 0;
                        }
                    case "metrics":
                        {
                            int top = arg == null ? 15 : int.Parse(arg);
                            var ms = Metrics.Members(an);
                            Console.WriteLine("方法(CC 由高到低):");
                            foreach (var m in ms.OrderByDescending(x => x.CC).Take(top))
                                Console.WriteLine("  CC=" + m.CC + " 巢狀=" + m.MaxNesting + " 行=" + m.Loc + " MI=" + m.MI.ToString("0") + "  " + m.Item.Display);
                            Console.WriteLine("類別(WMC 由高到低):");
                            foreach (var t in Metrics.Types(an, ms).OrderByDescending(x => x.WMC).Take(top))
                                Console.WriteLine("  WMC=" + t.WMC + " Ca=" + t.Ca + " Ce=" + t.Ce + " 成員=" + t.Members + "  " + t.Item.Display);
                            return 0;
                        }
                    case "dead":
                        {
                            var dd = Metrics.Dead(an);
                            foreach (var d in dd) Console.WriteLine("  [" + d.Confidence + "] " + d.Item.Kind + " " + d.Item.Display + "  — " + d.Reason);
                            Console.WriteLine("共 " + dd.Count + " 筆(高 " + dd.Count(x => x.Confidence == "高") + "、中 " + dd.Count(x => x.Confidence == "中") + "、低 " + dd.Count(x => x.Confidence == "低") + ")");
                            return 0;
                        }
                    case "cycles":
                        {
                            var cs = Metrics.Cycles(an);
                            foreach (var c in cs) Console.WriteLine("  [" + c.Level + "] " + c.Nodes.Count + " 個:" + string.Join("、", c.Nodes.Take(10)) + (c.Nodes.Count > 10 ? " …" : ""));
                            Console.WriteLine("共 " + cs.Count + " 個循環群組");
                            return 0;
                        }
                    case "dashboard":
                        {
                            var outPath = arg ?? "codeatlas-dashboard.html";
                            System.IO.File.WriteAllText(outPath, Report.Dashboard(an), new UTF8Encoding(true));
                            Console.WriteLine("已輸出:" + System.IO.Path.GetFullPath(outPath));
                            return 0;
                        }
                    case "search":
                        if (arg == null) { Console.WriteLine(Usage); return 2; }
                        var hits = an.Search(arg);
                        hits.ForEach(Console.WriteLine);
                        Console.WriteLine("共 " + hits.Count + " 筆");
                        return 0;
                }

                if (arg == null) { Console.WriteLine(Usage); return 2; }
                var found = an.Find(arg);
                if (found.Count == 0) { Console.WriteLine("找不到符號:" + arg); return 1; }
                if (args[0] == "find")
                {
                    foreach (var f in found) Console.WriteLine(f.Kind + " " + f.Display + "  " + System.IO.Path.GetFileName(f.File) + ":" + f.Line);
                    return 0;
                }
                if (found.Count > 1)
                {   // 先看有沒有剛好一個完全同名的;debtors 這類只對類別有意義的指令,優先選類別
                    var ex = found.Where(f => f.Display == arg).ToList(); if (ex.Count == 1) found = ex;
                    else if (args[0] == "debtors") { var ty = found.Where(f => f.Symbol is INamedTypeSymbol).ToList(); if (ty.Count == 1) found = ty; }
                }
                if (found.Count > 1)
                {
                    Console.WriteLine("名稱不唯一,請寫得更完整:");
                    foreach (var f in found) Console.WriteLine("  " + f.Display);
                    return 1;
                }
                var target = found[0];
                if (args[0] != "context" && args[0] != "body") Console.WriteLine(target.Kind + " " + target.Display);

                switch (args[0])
                {
                    case "refs":
                        var refs = an.References(target);
                        foreach (var g in refs.GroupBy(r => r.Kind))
                            Console.WriteLine("  [" + g.Key + "] " + g.Count() + " 處");
                        foreach (var r in refs)
                            Console.WriteLine("  " + r.Kind + "\t" + System.IO.Path.GetFileName(r.File) + ":" + r.Line + "\t在 " + r.EnclosingDisplay + "\t" + r.Text);
                        return 0;
                    case "calls":
                        depth = args.Length > 3 ? int.Parse(args[3]) : 2;
                        Console.WriteLine("被呼叫(誰呼叫它):");
                        PrintTree(an, target, depth, true, new HashSet<string> { target.Key }, "  ");
                        Console.WriteLine("呼叫(它呼叫誰):");
                        PrintTree(an, target, depth, false, new HashSet<string> { target.Key }, "  ");
                        return 0;
                    case "flow":
                        {
                            var fo = new FlowOptions { Expand = args.Length > 3 && int.TryParse(args[3], out depth) ? depth : 0 };
                            var flow = new FlowBuilder(an, fo).Build(target.Symbol, fo.Expand, new HashSet<string>());
                            Console.WriteLine("---- 循序圖 (Mermaid) ----\n" + FlowWriter.Sequence(target.Symbol, flow, false));
                            Console.WriteLine("---- 活動圖 (Mermaid 流程圖) ----\n" + FlowWriter.ActivityMermaid(flow));
                            Console.WriteLine("---- 循序圖 (PlantUML) ----\n" + FlowWriter.Sequence(target.Symbol, flow, true));
                            Console.WriteLine("---- 活動圖 (PlantUML) ----\n" + FlowWriter.ActivityPuml(flow));
                            return 0;
                        }
                    case "debtors":
                        {
                            var g = new GitInfo(); if (g.Problem != null) { Console.WriteLine(g.Problem); return 1; }
                            var list = g.Debtors(an, target, 100, 10);
                            foreach (var d in list)
                            {
                                var e = d.Editors;
                                Console.WriteLine("  " + d.Item.Kind + " " + d.Item.Display.Substring(d.Item.Display.IndexOf(target.Symbol.Name + ".") + target.Symbol.Name.Length + 1) + "  (第 " + d.Start + "–" + d.End + " 行)");
                                Console.WriteLine("      最後編輯:" + (e.Last == null ? (e.Note ?? "無紀錄") : e.Last.Author + " " + e.Last.Date + " " + e.Last.Hash + " " + e.Last.Subject)
                                    + (e.Previous.Count > 0 ? "\n      上幾代:" + string.Join("、", e.Previous.Select(p => p.Key + "(" + p.Value + ")")) : ""));
                            }
                            Console.WriteLine("沒寫註解的成員共 " + list.Count + " 個");
                            return 0;
                        }
                    case "context":
                        Console.WriteLine(Report.Context(an, target, args.Length > 3 ? int.Parse(args[3]) : 40));
                        return 0;
                    case "body":
                        {
                            int bf = 0, bt = 0;
                            if (args.Length > 3) int.TryParse(args[3], out bf);
                            if (args.Length > 4) int.TryParse(args[4], out bt);
                            Console.WriteLine(Report.Body(target, bf, bt));
                        }
                        return 0;
                    case "impact":
                        depth = args.Length > 3 ? int.Parse(args[3]) : 5;
                        var imp = an.Impact(target, depth);
                        foreach (var kv in imp) Console.WriteLine("  第 " + kv.Key + " 層  " + kv.Value.Kind + " " + kv.Value.Display);
                        Console.WriteLine("共影響 " + imp.Count + " 個成員");
                        return 0;
                }
                Console.WriteLine(Usage);
                return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[錯誤] " + ex.Message);
                return 1;
            }
        }

        static void PrintTree(Analyzer an, SymbolInfoItem node, int depth, bool incoming, HashSet<string> path, string indent)
        {
            if (depth == 0) return;
            var edges = incoming ? an.IncomingCalls(node) : an.OutgoingCalls(node);
            foreach (var g in edges.GroupBy(r => incoming ? r.EnclosingKey : r.TargetKey))
            {
                SymbolInfoItem next;
                var first = g.First();
                var found = an.Declared().TryGetValue(g.Key, out next);
                var name = found ? next.Display : (incoming ? first.EnclosingDisplay : g.Key);
                bool loop = path.Contains(g.Key);
                Console.WriteLine(indent + "← → ".Substring(0, 0) + (incoming ? "← " : "→ ") + name + "(" + g.Count() + " 處)" + (loop ? " [循環]" : ""));
                if (found && !loop)
                {
                    path.Add(g.Key);
                    PrintTree(an, next, depth - 1, incoming, path, indent + "  ");
                    path.Remove(g.Key);
                }
            }
        }
    }

    static class SymbolExt
    {
        public static IEnumerable<INamedTypeSymbol> GetAllTypes(this INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
                foreach (var x in WithNested(t)) yield return x;
            foreach (var n in ns.GetNamespaceMembers())
                foreach (var t in n.GetAllTypes()) yield return t;
        }

        static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol t)
        {
            yield return t;
            foreach (var n in t.GetTypeMembers())
                foreach (var x in WithNested(n)) yield return x;
        }
    }
}

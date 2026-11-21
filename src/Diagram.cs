using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    public class Edge
    {
        public string From, To, Cat, Group, Via;
    }

    /// <summary>
    /// 類別之間的關係,分四組:
    ///   inh  繼承(繼承、實作)
    ///   own  擁有(組合、聚合、關聯)
    ///   dep  依賴(其餘的使用關係)
    ///   nest 巢狀(外層類別包含內層類別)
    /// 同一對類別只保留最強的一種:繼承 > 擁有 > 依賴。
    /// </summary>
    public class Relations
    {
        public List<Edge> Edges = new List<Edge>();

        static IEnumerable<ITypeSymbol> Unwrap(ITypeSymbol t, bool[] coll)
        {
            var arr = t as IArrayTypeSymbol;
            if (arr != null) { coll[0] = true; foreach (var x in Unwrap(arr.ElementType, coll)) yield return x; yield break; }
            var nt = t as INamedTypeSymbol;
            if (nt != null && nt.IsGenericType && nt.SpecialType != SpecialType.System_Nullable_T && nt.ConstructedFrom.SpecialType != SpecialType.System_Nullable_T)
            {
                if (nt.SpecialType != SpecialType.System_String && nt.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable)) coll[0] = true;
                foreach (var a in nt.TypeArguments) foreach (var x in Unwrap(a, coll)) yield return x;
                yield break;
            }
            if (nt != null && nt.IsGenericType) { foreach (var a in nt.TypeArguments) foreach (var x in Unwrap(a, coll)) yield return x; yield break; }
            yield return t;
        }

        public static Relations Build(Analyzer an)
        {
            var rel = new Relations();
            var declared = an.Declared();
            var types = declared.Values.Where(d => d.Symbol is INamedTypeSymbol).ToList();
            var typeKeys = new HashSet<string>(types.Select(t => t.Key));

            // 擁有關係需要知道成員是由誰建立、或由建構子參數傳入
            var created = new HashSet<string>(); var fromParam = new HashSet<string>();
            foreach (var p in an.Projects)
                foreach (var tree in p.Compilation.SyntaxTrees)
                {
                    var model = p.Compilation.GetSemanticModel(tree);
                    foreach (var node in tree.GetRoot().DescendantNodes())
                    {
                        var vd = node as VariableDeclaratorSyntax;
                        if (vd != null && vd.Initializer != null && vd.Initializer.Value is BaseObjectCreationExpressionSyntax)
                        { var s = model.GetDeclaredSymbol(vd); if (s != null && s.Kind == SymbolKind.Field) created.Add(Analyzer.Key(s)); continue; }
                        var pd = node as PropertyDeclarationSyntax;
                        if (pd != null && pd.Initializer != null && pd.Initializer.Value is BaseObjectCreationExpressionSyntax)
                        { var s = model.GetDeclaredSymbol(pd); if (s != null) created.Add(Analyzer.Key(s)); continue; }
                        var asg = node as AssignmentExpressionSyntax;
                        if (asg != null && asg.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        {
                            var ls = model.GetSymbolInfo(asg.Left).Symbol;
                            if (ls == null || !(ls.Kind == SymbolKind.Field || ls.Kind == SymbolKind.Property)) continue;
                            var enc = Analyzer.Enclosing(model, node.SpanStart);
                            if (enc == null || enc.ContainingType == null || !SymbolEqualityComparer.Default.Equals(enc.ContainingType, ls.ContainingType)) continue;
                            if (asg.Right is BaseObjectCreationExpressionSyntax) created.Add(Analyzer.Key(ls));
                            else if (asg.Right is IdentifierNameSyntax && model.GetSymbolInfo(asg.Right).Symbol is IParameterSymbol) fromParam.Add(Analyzer.Key(ls));
                        }
                    }
                }

            var pair = new Dictionary<string, Edge>(); // 同一對類別只留最強關係
            Action<string, string, string, string, string> add = (from, to, cat, group, via) =>
            {
                if (from == to || !typeKeys.Contains(from) || !typeKeys.Contains(to)) return;
                var k = from + "→" + to + "|" + (group == "nest" ? "nest" : "x");
                Edge old;
                int rank(string c) { switch (c) { case "繼承": return 9; case "實作": return 8; case "組合": return 7; case "聚合": return 6; case "關聯": return 5; case "依賴": return 1; default: return 0; } }
                if (pair.TryGetValue(k, out old) && rank(old.Cat) >= rank(cat)) return;
                pair[k] = new Edge { From = from, To = to, Cat = cat, Group = group, Via = via };
            };

            foreach (var d in types)
            {
                var t = (INamedTypeSymbol)d.Symbol;
                if (t.BaseType != null && t.BaseType.SpecialType != SpecialType.System_Object && t.TypeKind != TypeKind.Interface)
                    add(d.Key, Analyzer.Key(t.BaseType), "繼承", "inh", null);
                foreach (var i in t.Interfaces) add(d.Key, Analyzer.Key(i), t.TypeKind == TypeKind.Interface ? "繼承" : "實作", "inh", null);
                if (t.ContainingType != null) add(Analyzer.Key(t.ContainingType), d.Key, "巢狀", "nest", null);

                foreach (var m in t.GetMembers())
                {
                    if (m.IsImplicitlyDeclared) continue;
                    ITypeSymbol mt = (m as IFieldSymbol) != null ? ((IFieldSymbol)m).Type : (m as IPropertySymbol) != null ? ((IPropertySymbol)m).Type : null;
                    if (mt == null) continue;
                    var coll = new bool[1];
                    foreach (var x in Unwrap(mt, coll))
                    {
                        if (!(x is INamedTypeSymbol)) continue;
                        var mk = Analyzer.Key(m);
                        string cat = coll[0] ? "聚合" : created.Contains(mk) ? "組合" : fromParam.Contains(mk) ? "聚合" : "關聯";
                        add(d.Key, Analyzer.Key(x), cat, "own", m.Name);
                    }
                }
            }

            var strong = new HashSet<string>(pair.Values.Where(e => e.Group != "nest").Select(e => e.From + "→" + e.To));
            foreach (var r in an.AllRefs())
            {
                if (r.EnclosingTypeKey == null || r.TargetTypeKey == null) continue;
                if (strong.Contains(r.EnclosingTypeKey + "→" + r.TargetTypeKey)) continue;
                add(r.EnclosingTypeKey, r.TargetTypeKey, "依賴", "dep", null);
            }
            rel.Edges = pair.Values.ToList();
            return rel;
        }
    }

    public class DiagramOptions
    {
        public string Namespace, Focus;
        public int Inh = 2, Own = 1, Dep = 1, Nest = 2, TopN = 8, MaxNodes = 150;
        public bool ShowInh = true, ShowOwn = true, ShowDep = false, ShowNest = true, HidePrivate;

        public static DiagramOptions FromQuery(System.Collections.Specialized.NameValueCollection q)
        {
            Func<string, int, int> i = (k, d) => { int v; return int.TryParse(q[k], out v) ? Math.Max(0, Math.Min(v, 50)) : d; };
            Func<string, bool, bool> b = (k, d) => q[k] == null ? d : (q[k] == "1" || q[k] == "true");
            return new DiagramOptions
            {
                Namespace = string.IsNullOrWhiteSpace(q["ns"]) ? null : q["ns"], Focus = string.IsNullOrWhiteSpace(q["focus"]) ? null : q["focus"].Trim(),
                Inh = i("inh", 2), Own = i("own", 1), Dep = i("dep", 1), Nest = i("nest", 2), TopN = i("topn", 8), MaxNodes = Math.Max(5, i("max", 150)),
                ShowInh = b("showInh", true), ShowOwn = b("showOwn", true), ShowDep = b("showDep", false), ShowNest = b("showNest", true), HidePrivate = b("hidePrivate", false)
            };
        }
    }

    public class DiagramBuilder
    {
        readonly Analyzer an; readonly Relations rel;
        static readonly SymbolDisplayFormat Short = SymbolDisplayFormat.MinimallyQualifiedFormat;

        public DiagramBuilder(Analyzer an) { this.an = an; rel = Relations.Build(an); }

        public class Selection { public List<SymbolInfoItem> Nodes = new List<SymbolInfoItem>(); public List<Edge> Edges = new List<Edge>(); public bool Truncated; public string Error; public string FocusKey; }

        static string Ns(SymbolInfoItem t) { return t.Symbol.ContainingNamespace.ToDisplayString(); }

        public SymbolInfoItem FindType(string q)
        {
            var c = an.Find(q).Where(x => x.Symbol is INamedTypeSymbol).ToList();
            return c.FirstOrDefault(x => x.Display == q) ?? c.FirstOrDefault(x => x.Symbol.Name == q) ?? c.FirstOrDefault();
        }

        public Selection Select(DiagramOptions o)
        {
            var sel = new Selection();
            var types = an.Declared().Values.Where(d => d.Symbol is INamedTypeSymbol).ToDictionary(d => d.Key);
            var groups = new HashSet<string>();
            if (o.ShowInh) groups.Add("inh"); if (o.ShowOwn) groups.Add("own"); if (o.ShowDep) groups.Add("dep"); if (o.ShowNest) groups.Add("nest");
            var edges = rel.Edges.Where(e => groups.Contains(e.Group)).ToList();

            var keys = new HashSet<string>();
            if (o.Focus != null)
            {
                var f = FindType(o.Focus);
                if (f == null) { sel.Error = "找不到類別:" + o.Focus; return sel; }
                sel.FocusKey = f.Key; keys.Add(f.Key);
                Action<string, int, bool, bool> bfs = (group, depth, fwd, back) =>
                {
                    var frontier = new HashSet<string> { f.Key }; var seen = new HashSet<string> { f.Key };
                    for (int d = 0; d < depth && frontier.Count > 0; d++)
                    {
                        var next = new HashSet<string>();
                        foreach (var e in edges.Where(e => e.Group == group))
                        {
                            if (fwd && frontier.Contains(e.From) && seen.Add(e.To)) next.Add(e.To);
                            if (back && frontier.Contains(e.To) && seen.Add(e.From)) next.Add(e.From);
                        }
                        foreach (var n in next) keys.Add(n);
                        frontier = next;
                    }
                };
                if (o.ShowInh) { bfs("inh", o.Inh, true, false); bfs("inh", o.Inh, false, true); }
                if (o.ShowOwn) bfs("own", o.Own, true, true);
                if (o.ShowDep) bfs("dep", o.Dep, true, true);
                if (o.ShowNest) bfs("nest", o.Nest, true, true);
            }
            else
            {
                var universe = types.Values.Where(t => o.Namespace == null || Ns(t) == o.Namespace || Ns(t).StartsWith(o.Namespace + ".")).ToList();
                var deg = new Dictionary<string, int>();
                foreach (var e in edges) { deg[e.From] = (deg.ContainsKey(e.From) ? deg[e.From] : 0) + 1; deg[e.To] = (deg.ContainsKey(e.To) ? deg[e.To] : 0) + 1; }
                var ordered = universe.OrderByDescending(t => deg.ContainsKey(t.Key) ? deg[t.Key] : 0).ThenBy(t => t.Display).ToList();
                if (ordered.Count > o.MaxNodes) { sel.Truncated = true; ordered = ordered.Take(o.MaxNodes).ToList(); }
                foreach (var t in ordered) keys.Add(t.Key);
            }
            sel.Nodes = keys.Where(types.ContainsKey).Select(k => types[k]).OrderBy(t => t.Display).ToList();
            sel.Edges = edges.Where(e => keys.Contains(e.From) && keys.Contains(e.To)).ToList();
            return sel;
        }

        // ---------- 成員 ----------
        static string Sign(ISymbol s)
        {
            switch (s.DeclaredAccessibility)
            {
                case Accessibility.Public: return "+";
                case Accessibility.Private: return "-";
                case Accessibility.Protected: return "#";
                default: return "~";
            }
        }

        static int AccOrder(ISymbol s) { return s.DeclaredAccessibility == Accessibility.Public ? 0 : s.DeclaredAccessibility == Accessibility.Protected ? 1 : s.DeclaredAccessibility == Accessibility.Private ? 3 : 2; }

        class Sec { public List<string> Items = new List<string>(); public int More; public int Total; }

        static Sec Section(IEnumerable<ISymbol> src, Func<ISymbol, string> fmt, DiagramOptions o)
        {
            var all = src.Where(s => !s.IsImplicitlyDeclared && !(o.HidePrivate && s.DeclaredAccessibility == Accessibility.Private))
                .OrderBy(AccOrder).ThenBy(s => s.Name).ToList();
            return new Sec { Total = all.Count, Items = all.Take(o.TopN).Select(fmt).ToList(), More = Math.Max(0, all.Count - o.TopN) };
        }

        static string MethodText(IMethodSymbol m)
        {
            var name = m.MethodKind == MethodKind.Constructor ? m.ContainingType.Name : m.Name;
            var ps = string.Join(", ", m.Parameters.Select(p => p.Type.ToDisplayString(Short)));
            return Sign(m) + " " + name + "(" + ps + ")" + (m.MethodKind == MethodKind.Constructor ? "" : " : " + m.ReturnType.ToDisplayString(Short));
        }

        static Dictionary<string, Sec> Sections(INamedTypeSymbol t, DiagramOptions o)
        {
            var ms = t.GetMembers().ToList();
            return new Dictionary<string, Sec>
            {
                { "fields", Section(ms.OfType<IFieldSymbol>().Where(f => t.TypeKind != TypeKind.Enum), s => Sign(s) + " " + s.Name + " : " + ((IFieldSymbol)s).Type.ToDisplayString(Short), o) },
                { "props", Section(ms.OfType<IPropertySymbol>(), s => Sign(s) + " " + s.Name + " : " + ((IPropertySymbol)s).Type.ToDisplayString(Short), o) },
                { "methods", Section(ms.OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary || m.MethodKind == MethodKind.Constructor
                        || m.MethodKind == MethodKind.UserDefinedOperator), s => MethodText((IMethodSymbol)s), o) },
                { "enums", Section(ms.OfType<IFieldSymbol>().Where(f => t.TypeKind == TypeKind.Enum), s => s.Name, o) }
            };
        }

        public object ToJson(Selection sel, DiagramOptions o)
        {
            var nodes = sel.Nodes.Select(n =>
            {
                var t = (INamedTypeSymbol)n.Symbol;
                var secs = Sections(t, o);
                return new
                {
                    id = n.Key, name = t.ToDisplayString(Short), ns = Ns(n), kind = n.Kind, file = n.File, line = n.Line, focus = n.Key == sel.FocusKey,
                    fields = secs["fields"].Items, fieldsMore = secs["fields"].More,
                    props = secs["props"].Items, propsMore = secs["props"].More,
                    methods = secs["methods"].Items, methodsMore = secs["methods"].More,
                    enums = secs["enums"].Items, enumsMore = secs["enums"].More
                };
            }).ToList();
            var edges = sel.Edges.Select((e, i) => new { id = "e" + i, source = e.From, target = e.To, cat = e.Cat, group = e.Group, via = e.Via }).ToList();
            return new { nodes, edges, truncated = sel.Truncated, error = sel.Error, total = an.Declared().Values.Count(d => d.Symbol is INamedTypeSymbol) };
        }

        // ---------- Mermaid ----------
        public string ToMermaid(Selection sel, DiagramOptions o)
        {
            var id = new Dictionary<string, string>(); int n = 0;
            foreach (var t in sel.Nodes) id[t.Key] = "C" + (++n) + "_" + new string(t.Symbol.Name.Where(char.IsLetterOrDigit).ToArray());
            Func<string, string> clean = s => s.Replace("<", "~").Replace(">", "~").Replace("{", "(").Replace("}", ")").Replace("\"", "'");
            var sb = new StringBuilder("classDiagram\n");
            foreach (var t in sel.Nodes)
            {
                var ts = (INamedTypeSymbol)t.Symbol; var secs = Sections(ts, o);
                sb.Append("  class " + id[t.Key] + "[\"" + clean(ts.ToDisplayString(Short)) + "\"] {\n");
                if (ts.TypeKind == TypeKind.Interface) sb.Append("    <<interface>>\n"); else if (ts.TypeKind == TypeKind.Enum) sb.Append("    <<enumeration>>\n");
                foreach (var k in new[] { "enums", "fields", "props", "methods" })
                {
                    foreach (var it in secs[k].Items) sb.Append("    " + clean(it) + "\n");
                    if (secs[k].More > 0) sb.Append("    … 另 " + secs[k].More + " 項\n");
                }
                sb.Append("  }\n");
            }
            foreach (var e in sel.Edges)
            {
                string a = id[e.From], b = id[e.To];
                switch (e.Cat)
                {
                    case "繼承": sb.Append("  " + b + " <|-- " + a + "\n"); break;
                    case "實作": sb.Append("  " + b + " <|.. " + a + "\n"); break;
                    case "組合": sb.Append("  " + a + " *-- " + b + "\n"); break;
                    case "聚合": sb.Append("  " + a + " o-- " + b + "\n"); break;
                    case "關聯": sb.Append("  " + a + " --> " + b + "\n"); break;
                    case "依賴": sb.Append("  " + a + " ..> " + b + "\n"); break;
                    case "巢狀": sb.Append("  " + a + " -- " + b + " : 巢狀\n"); break;
                }
            }
            return sb.ToString();
        }

        // ---------- 檔案大綱與中繼資料 ----------
        public object Outline()
        {
            var files = new Dictionary<string, List<object>>();
            foreach (var d in an.Declared().Values.Where(d => d.Symbol is INamedTypeSymbol))
            {
                var t = (INamedTypeSymbol)d.Symbol;
                var names = t.GetMembers().Where(m => !m.IsImplicitlyDeclared && !(m is INamedTypeSymbol)).Select(m => m.Name).Distinct().ToList();
                foreach (var r in t.DeclaringSyntaxReferences)
                {
                    var f = r.SyntaxTree.FilePath; List<object> l;
                    if (!files.TryGetValue(f, out l)) files[f] = l = new List<object>();
                    l.Add(new { key = d.Key, name = t.ToDisplayString(Short), kind = d.Kind, line = r.SyntaxTree.GetLineSpan(r.Span).StartLinePosition.Line + 1, members = names });
                }
            }
            return files.OrderBy(kv => kv.Key).Select(kv => new { file = kv.Key, types = kv.Value }).ToList();
        }

        public object Meta()
        {
            var ns = an.Declared().Values.Where(d => d.Symbol is INamedTypeSymbol).GroupBy(Ns).OrderBy(g => g.Key)
                .Select(g => new { name = g.Key, count = g.Count() }).ToList();
            return new { namespaces = ns, projects = an.Projects.Select(p => p.Name).ToList() };
        }
    }
}

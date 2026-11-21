using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    public class MemberMetric
    {
        public SymbolInfoItem Item;
        public int CC, MaxNesting, Loc;
        public double MI;
    }

    public class TypeMetric
    {
        public SymbolInfoItem Item;
        public string Namespace;
        public int Members, WMC, Ca, Ce, Loc;
        public double AvgMI;
        public double Instability { get { return Ca + Ce == 0 ? 0 : (double)Ce / (Ca + Ce); } }
    }

    public class DeadItem
    {
        public SymbolInfoItem Item;
        public string Confidence, Reason;
    }

    public class CycleGroup
    {
        public string Level;
        public List<string> Nodes;
    }

    public static class Metrics
    {
        // ---------- 成員度量 ----------
        public static List<MemberMetric> Members(Analyzer an)
        {
            var res = new Dictionary<string, MemberMetric>();
            var counts = new Dictionary<string, int>();
            var declared = an.Declared();
            foreach (var p in an.Projects)
                foreach (var tree in p.Compilation.SyntaxTrees)
                {
                    var model = p.Compilation.GetSemanticModel(tree);
                    var text = tree.GetText();
                    foreach (var node in tree.GetRoot().DescendantNodes())
                    {
                        SyntaxNode body = null; ISymbol sym = null;
                        var md = node as BaseMethodDeclarationSyntax;
                        if (md != null) { body = (SyntaxNode)md.Body ?? md.ExpressionBody; if (body != null) sym = model.GetDeclaredSymbol(md); }
                        else if (node is AccessorDeclarationSyntax)
                        {
                            var ad = (AccessorDeclarationSyntax)node;
                            body = (SyntaxNode)ad.Body ?? ad.ExpressionBody;
                            if (body != null) { var ms = model.GetDeclaredSymbol(ad) as IMethodSymbol; sym = ms == null ? null : (ms.AssociatedSymbol ?? ms); }
                        }
                        else if (node is PropertyDeclarationSyntax && ((PropertyDeclarationSyntax)node).ExpressionBody != null)
                        { body = ((PropertyDeclarationSyntax)node).ExpressionBody; sym = model.GetDeclaredSymbol(node); }
                        if (body == null || sym == null) continue;

                        var key = Analyzer.Key(sym);
                        SymbolInfoItem item;
                        if (!declared.TryGetValue(key, out item)) continue;

                        int cc = Complexity(body), nest = Nesting(body), loc = Loc(text, node);
                        double mi = MaintainabilityIndex(body, cc, loc);
                        MemberMetric m;
                        if (!res.TryGetValue(key, out m)) { res[key] = new MemberMetric { Item = item, CC = cc, MaxNesting = nest, Loc = loc, MI = mi }; counts[key] = 1; }
                        else
                        {   // 屬性有多個存取子:CC 合併(各存取子 CC-1 相加再 +1)
                            m.CC += cc - 1; m.MaxNesting = Math.Max(m.MaxNesting, nest); m.Loc += loc; m.MI = Math.Min(m.MI, mi);
                        }
                    }
                }
            return res.Values.ToList();
        }

        static int Complexity(SyntaxNode body)
        {
            int cc = 1;
            foreach (var n in body.DescendantNodesAndSelf())
            {
                switch (n.Kind())
                {
                    case SyntaxKind.IfStatement: case SyntaxKind.WhileStatement: case SyntaxKind.DoStatement:
                    case SyntaxKind.ForStatement: case SyntaxKind.ForEachStatement: case SyntaxKind.ForEachVariableStatement:
                    case SyntaxKind.CaseSwitchLabel: case SyntaxKind.CasePatternSwitchLabel: case SyntaxKind.SwitchExpressionArm:
                    case SyntaxKind.CatchClause: case SyntaxKind.ConditionalExpression:
                    case SyntaxKind.LogicalAndExpression: case SyntaxKind.LogicalOrExpression:
                    case SyntaxKind.CoalesceExpression: case SyntaxKind.CoalesceAssignmentExpression:
                        cc++; break;
                }
            }
            return cc;
        }

        static bool IsNest(SyntaxNode n)
        {
            switch (n.Kind())
            {
                case SyntaxKind.IfStatement: return !(n.Parent is ElseClauseSyntax); // else if 不算再多一層
                case SyntaxKind.WhileStatement: case SyntaxKind.DoStatement: case SyntaxKind.ForStatement:
                case SyntaxKind.ForEachStatement: case SyntaxKind.ForEachVariableStatement: case SyntaxKind.SwitchStatement:
                case SyntaxKind.TryStatement: case SyntaxKind.LockStatement: case SyntaxKind.UsingStatement: case SyntaxKind.FixedStatement:
                    return true;
            }
            return false;
        }

        static int Nesting(SyntaxNode body)
        {
            int max = 0;
            foreach (var n in body.DescendantNodes().Where(IsNest))
            {
                int d = 0;
                for (var a = n; a != null && a != body.Parent; a = a.Parent) if (IsNest(a)) d++;
                max = Math.Max(max, d);
            }
            return max;
        }

        static int Loc(Microsoft.CodeAnalysis.Text.SourceText text, SyntaxNode node)
        {
            int n = 0;
            var ls = text.Lines.GetLinePosition(node.Span.Start).Line;
            var le = text.Lines.GetLinePosition(node.Span.End).Line;
            for (int i = ls; i <= le; i++) if (!string.IsNullOrWhiteSpace(text.Lines[i].ToString())) n++;
            return n;
        }

        /// <summary>
        /// 維護性指數(0~100):以標準公式 171 − 5.2·ln(V) − 0.23·CC − 16.2·ln(LOC) 換算。
        /// Halstead 體積 V 由語法標記估算(識別字與字面值視為運算元,其餘視為運算子),
        /// 因此數值是近似值,與 Visual Studio 內建的結果會有差異。
        /// </summary>
        static double MaintainabilityIndex(SyntaxNode body, int cc, int loc)
        {
            int total = 0; var distinct = new HashSet<string>();
            foreach (var t in body.DescendantTokens())
            {
                total++; distinct.Add(t.Kind() + ":" + t.Text);
            }
            double v = total * Math.Log(Math.Max(distinct.Count, 2), 2);
            double mi = (171 - 5.2 * Math.Log(Math.Max(v, 1)) - 0.23 * cc - 16.2 * Math.Log(Math.Max(loc, 1))) * 100.0 / 171.0;
            return Math.Max(0, Math.Min(100, mi));
        }

        // ---------- 類別度量 ----------
        public static List<TypeMetric> Types(Analyzer an, List<MemberMetric> members)
        {
            var declared = an.Declared();
            var refs = an.AllRefs();
            var ce = new Dictionary<string, HashSet<string>>();
            var ca = new Dictionary<string, HashSet<string>>();
            foreach (var r in refs)
            {
                if (r.EnclosingTypeKey == null || r.TargetTypeKey == null || r.EnclosingTypeKey == r.TargetTypeKey) continue;
                if (!declared.ContainsKey(r.EnclosingTypeKey) || !declared.ContainsKey(r.TargetTypeKey)) continue;
                HashSet<string> s;
                if (!ce.TryGetValue(r.EnclosingTypeKey, out s)) ce[r.EnclosingTypeKey] = s = new HashSet<string>();
                s.Add(r.TargetTypeKey);
                if (!ca.TryGetValue(r.TargetTypeKey, out s)) ca[r.TargetTypeKey] = s = new HashSet<string>();
                s.Add(r.EnclosingTypeKey);
            }
            var byType = members.Where(m => m.Item.Symbol.ContainingType != null)
                .GroupBy(m => Analyzer.Key(m.Item.Symbol.ContainingType)).ToDictionary(g => g.Key, g => g.ToList());
            var res = new List<TypeMetric>();
            foreach (var d in declared.Values.Where(d => d.Symbol is INamedTypeSymbol))
            {
                List<MemberMetric> ms;
                byType.TryGetValue(d.Key, out ms);
                ms = ms ?? new List<MemberMetric>();
                HashSet<string> a, e;
                res.Add(new TypeMetric
                {
                    Item = d, Namespace = d.Symbol.ContainingNamespace.ToDisplayString(),
                    Members = declared.Values.Count(x => x.Symbol.ContainingType != null && Analyzer.Key(x.Symbol.ContainingType) == d.Key),
                    WMC = ms.Sum(m => m.CC), Loc = ms.Sum(m => m.Loc),
                    AvgMI = ms.Count == 0 ? 100 : ms.Average(m => m.MI),
                    Ca = ca.TryGetValue(d.Key, out a) ? a.Count : 0,
                    Ce = ce.TryGetValue(d.Key, out e) ? e.Count : 0
                });
            }
            return res;
        }

        // ---------- 死碼偵測 ----------
        static int AccRank(ISymbol s)
        {
            int rank = 0; // 0 私有(整個專案內可見度最小)、1 內部、2 公開
            for (var c = s; c != null && !(c is INamespaceSymbol); c = c.ContainingSymbol)
            {
                int r;
                switch (c.DeclaredAccessibility)
                {
                    case Accessibility.Private: r = 0; break;
                    case Accessibility.Internal: case Accessibility.ProtectedAndInternal: r = 1; break;
                    default: r = 2; break;
                }
                if (c.DeclaredAccessibility == Accessibility.NotApplicable) r = c.ContainingType != null ? 0 : 1;
                if (c == s) rank = r; else rank = Math.Min(rank, r);
            }
            return rank;
        }

        public static List<DeadItem> Dead(Analyzer an)
        {
            var declared = an.Declared();
            var refs = an.AllRefs();
            var byTarget = refs.GroupBy(r => r.TargetKey).ToDictionary(g => g.Key, g => g.ToList());
            var implemented = new HashSet<string>();
            foreach (var d in declared.Values.Where(d => d.Symbol is INamedTypeSymbol))
            {
                var t = (INamedTypeSymbol)d.Symbol;
                foreach (var itf in t.AllInterfaces)
                    foreach (var im in itf.GetMembers())
                    {
                        var impl = t.FindImplementationForInterfaceMember(im);
                        if (impl != null) implemented.Add(Analyzer.Key(impl));
                    }
            }

            var res = new List<DeadItem>();
            foreach (var d in declared.Values)
            {
                var s = d.Symbol;
                if (s.IsImplicitlyDeclared || s.IsOverride || implemented.Contains(d.Key)) continue;
                if (s.ContainingType != null && s.ContainingType.TypeKind == TypeKind.Interface) continue;
                if (s.ContainingType != null && s.ContainingType.TypeKind == TypeKind.Enum) continue;
                if (s.IsAbstract) continue;
                var nt = s as INamedTypeSymbol; // 含程式進入點 Main 的型別不算死碼
                if (nt != null && nt.GetMembers("Main").OfType<IMethodSymbol>().Any(x => x.IsStatic)) continue;
                var ms = s as IMethodSymbol;
                if (ms != null)
                {
                    if (ms.MethodKind == MethodKind.Constructor && ms.DeclaredAccessibility != Accessibility.Private) continue;
                    if (ms.MethodKind == MethodKind.Destructor || ms.MethodKind == MethodKind.UserDefinedOperator || ms.MethodKind == MethodKind.Conversion) continue;
                    if (ms.IsStatic && ms.Name == "Main") continue;
                    if (ms.ExplicitInterfaceImplementations.Length > 0) continue;
                }
                var ps = s as IPropertySymbol;
                if (ps != null && ps.ExplicitInterfaceImplementations.Length > 0) continue;

                List<Ref> rs;
                byTarget.TryGetValue(d.Key, out rs);
                var outside = (rs ?? new List<Ref>()).Where(r => r.EnclosingKey != d.Key).ToList();
                // 型別:自己成員內部的使用不算「有人用到」
                if (s is INamedTypeSymbol) outside = outside.Where(r => r.EnclosingTypeKey != d.Key).ToList();

                if (outside.Count > 0)
                {
                    if ((s.Kind == SymbolKind.Field || s.Kind == SymbolKind.Property) && AccRank(s) == 0
                        && outside.All(r => r.Kind == "寫") && !(s is IFieldSymbol f0 && f0.IsConst))
                        res.Add(new DeadItem { Item = d, Confidence = "中", Reason = "只有被寫入、從未被讀取" });
                    continue;
                }

                int rank = AccRank(s);
                int level = rank == 0 ? 0 : rank == 1 ? 1 : 2; // 0 高、1 中、2 低
                var reasons = new List<string> { rank == 0 ? "私有且整個方案內沒有任何參照" : rank == 1 ? "內部成員且方案內沒有參照" : "公開成員,方案內沒有參照(可能被外部程式使用)" };
                if (s.GetAttributes().Length > 0) { level = Math.Min(2, level + 1); reasons.Add("有屬性標註,可能由框架或反射使用"); }
                if (ms != null && ms.Name.Contains("_") && ms.Parameters.Length == 2) { level = Math.Min(2, level + 1); reasons.Add("名稱像事件處理常式"); }
                res.Add(new DeadItem { Item = d, Confidence = new[] { "高", "中", "低" }[level], Reason = string.Join(";", reasons) });
            }
            return res.OrderBy(x => x.Confidence == "高" ? 0 : x.Confidence == "中" ? 1 : 2).ThenBy(x => x.Item.Display).ToList();
        }

        // ---------- 循環相依 ----------
        public static List<CycleGroup> Cycles(Analyzer an)
        {
            var declared = an.Declared();
            var refs = an.AllRefs();
            var res = new List<CycleGroup>();
            Func<string, string> ns = k => declared[k].Symbol.ContainingNamespace.ToDisplayString();
            Func<string, string> proj = k => declared[k].Project;

            var levels = new List<KeyValuePair<string, Func<string, string>>>
            {
                new KeyValuePair<string, Func<string, string>>("專案", proj),
                new KeyValuePair<string, Func<string, string>>("命名空間", ns),
                new KeyValuePair<string, Func<string, string>>("型別", k => declared[k].Display)
            };
            foreach (var lv in levels)
            {
                var g = new Dictionary<string, HashSet<string>>();
                foreach (var r in refs)
                {
                    if (r.EnclosingTypeKey == null || r.TargetTypeKey == null) continue;
                    if (!declared.ContainsKey(r.EnclosingTypeKey) || !declared.ContainsKey(r.TargetTypeKey)) continue;
                    string a = lv.Value(r.EnclosingTypeKey), b = lv.Value(r.TargetTypeKey);
                    if (a == b) continue;
                    HashSet<string> s;
                    if (!g.TryGetValue(a, out s)) g[a] = s = new HashSet<string>();
                    s.Add(b);
                    if (!g.ContainsKey(b)) g[b] = new HashSet<string>();
                }
                foreach (var scc in Tarjan(g).Where(c => c.Count > 1).OrderByDescending(c => c.Count))
                    res.Add(new CycleGroup { Level = lv.Key, Nodes = scc.OrderBy(x => x).ToList() });
            }
            return res;
        }

        static List<List<string>> Tarjan(Dictionary<string, HashSet<string>> g)
        {
            var result = new List<List<string>>();
            var t = new Thread(() =>
            {
                int idx = 0;
                var index = new Dictionary<string, int>(); var low = new Dictionary<string, int>();
                var stack = new Stack<string>(); var on = new HashSet<string>();
                Action<string> visit = null;
                visit = v =>
                {
                    index[v] = low[v] = idx++; stack.Push(v); on.Add(v);
                    foreach (var w in g[v])
                    {
                        if (!index.ContainsKey(w)) { visit(w); low[v] = Math.Min(low[v], low[w]); }
                        else if (on.Contains(w)) low[v] = Math.Min(low[v], index[w]);
                    }
                    if (low[v] == index[v])
                    {
                        var comp = new List<string>(); string x;
                        do { x = stack.Pop(); on.Remove(x); comp.Add(x); } while (x != v);
                        result.Add(comp);
                    }
                };
                foreach (var v in g.Keys.ToList()) if (!index.ContainsKey(v)) visit(v);
            }, 256 * 1024 * 1024); // 大型方案遞迴很深,給足堆疊
            t.Start(); t.Join();
            return result;
        }
    }
}

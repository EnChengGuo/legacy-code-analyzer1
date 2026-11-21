using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    /// <summary>呼叫樹、狀態圖、資料流。狀態圖與資料流較耗時且是推測性質,由使用者勾選才產生。</summary>
    public static class FlowExtras
    {
        static string Mm(string s) { return (s ?? "").Replace(";", ",").Replace("#", "＃").Replace("\"", "'"); }

        // ===== 呼叫樹 =====
        public static object CallTree(Analyzer an, SymbolInfoItem root, int depth)
        {
            var text = new StringBuilder(); var mm = new StringBuilder("flowchart LR\n"); int n = 0;
            var ids = new Dictionary<string, string>();
            Func<string, string, string> id = (key, label) =>
            {
                string v; if (ids.TryGetValue(key, out v)) return v;
                v = "T" + (++n); ids[key] = v; mm.Append("  " + v + "[\"" + Mm(label) + "\"]\n"); return v;
            };
            Action<SymbolInfoItem, int, string, HashSet<string>> walk = null;
            walk = (node, d, indent, path) =>
            {
                if (d == 0) return;
                foreach (var g in an.OutgoingCalls(node).GroupBy(r => r.TargetKey))
                {
                    SymbolInfoItem next; var found = an.Declared().TryGetValue(g.Key, out next);
                    var label = found ? next.Display : g.Key.Substring(g.Key.IndexOf(':') + 1);
                    bool loop = path.Contains(g.Key);
                    text.Append(indent + "→ " + label + (g.Count() > 1 ? "(×" + g.Count() + ")" : "") + (loop ? " [循環]" : "") + "\n");
                    mm.Append("  " + id(node.Key, node.Display) + " --> " + id(g.Key, label) + "\n");
                    if (found && !loop) { path.Add(g.Key); walk(next, d - 1, indent + "  ", path); path.Remove(g.Key); }
                }
            };
            text.Append(root.Display + "\n");
            id(root.Key, root.Display);
            walk(root, depth, "  ", new HashSet<string> { root.Key });
            return new { text = text.ToString(), mermaid = mm.ToString() };
        }

        // ===== 狀態圖 =====
        static SemanticModel ModelFor(Analyzer an, SyntaxTree t)
        {
            foreach (var p in an.Projects) if (p.Compilation.SyntaxTrees.Contains(t)) return p.Compilation.GetSemanticModel(t);
            return null;
        }

        static string EnumConst(SemanticModel m, ExpressionSyntax e)
        {
            var f = m.GetSymbolInfo(e).Symbol as IFieldSymbol;
            return f != null && f.ContainingType != null && f.ContainingType.TypeKind == TypeKind.Enum ? f.Name : null;
        }

        static void CondStates(SemanticModel m, ExpressionSyntax cond, ISymbol member, List<string> into)
        {
            foreach (var b in cond.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Where(b => b.IsKind(SyntaxKind.EqualsExpression)))
            {
                var ls = m.GetSymbolInfo(b.Left).Symbol; var rs = m.GetSymbolInfo(b.Right).Symbol;
                if (ls != null && SymbolEqualityComparer.Default.Equals(ls.OriginalDefinition, member.OriginalDefinition)) { var c = EnumConst(m, b.Right); if (c != null) into.Add(c); }
                else if (rs != null && SymbolEqualityComparer.Default.Equals(rs.OriginalDefinition, member.OriginalDefinition)) { var c = EnumConst(m, b.Left); if (c != null) into.Add(c); }
            }
        }

        public static object State(Analyzer an, INamedTypeSymbol type, string memberName)
        {
            var cands = type.GetMembers().Where(x => (x is IFieldSymbol && !x.IsImplicitlyDeclared && ((IFieldSymbol)x).Type.TypeKind == TypeKind.Enum)
                || (x is IPropertySymbol && ((IPropertySymbol)x).Type.TypeKind == TypeKind.Enum)).ToList();
            if (cands.Count == 0) return new { error = "類別 " + type.Name + " 沒有列舉型別的欄位或屬性,看不出狀態機。", members = new string[0] };
            var chosen = cands.FirstOrDefault(c => c.Name == memberName) ?? cands[0];
            var trans = new HashSet<string>(); var initial = new HashSet<string>(); var states = new HashSet<string>();
            foreach (var r in type.DeclaringSyntaxReferences.Select(x => x.SyntaxTree).Distinct())
            {
                var m = ModelFor(an, r); if (m == null) continue;
                foreach (var node in r.GetRoot().DescendantNodes())
                {
                    // 初始值
                    var vd = node as VariableDeclaratorSyntax;
                    if (vd != null && vd.Initializer != null && SymbolEqualityComparer.Default.Equals(m.GetDeclaredSymbol(vd), chosen)) { var c = EnumConst(m, vd.Initializer.Value); if (c != null) initial.Add(c); }
                    var asg = node as AssignmentExpressionSyntax;
                    if (asg == null || !asg.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;
                    var ls = m.GetSymbolInfo(asg.Left).Symbol;
                    if (ls == null || !SymbolEqualityComparer.Default.Equals(ls.OriginalDefinition, chosen.OriginalDefinition)) continue;
                    var target = EnumConst(m, asg.Right) ?? "UNKNOWN";
                    var enc = Analyzer.Enclosing(m, node.SpanStart);
                    if (enc is IMethodSymbol && ((IMethodSymbol)enc).MethodKind == MethodKind.Constructor) { initial.Add(target); continue; }
                    var srcs = new List<string>();
                    SyntaxNode child = node;
                    for (var a = node.Parent; a != null; child = a, a = a.Parent)
                    {
                        var ifs = a as IfStatementSyntax;
                        if (ifs != null && ifs.Statement.Span.Contains(child.Span)) CondStates(m, ifs.Condition, chosen, srcs);
                        var sec = a as SwitchSectionSyntax;
                        if (sec != null)
                        {
                            var sw = sec.Parent as SwitchStatementSyntax; var ss = sw == null ? null : m.GetSymbolInfo(sw.Expression).Symbol;
                            if (ss != null && SymbolEqualityComparer.Default.Equals(ss.OriginalDefinition, chosen.OriginalDefinition))
                                foreach (var lb in sec.Labels.OfType<CaseSwitchLabelSyntax>()) { var c = EnumConst(m, lb.Value); if (c != null) srcs.Add(c); }
                        }
                    }
                    if (srcs.Count == 0) srcs.Add("ANY");
                    var meth = enc == null ? "?" : enc.Name;
                    foreach (var s in srcs.Distinct()) { trans.Add(s + "\t" + target + "\t" + meth); states.Add(s); }
                    states.Add(target);
                }
            }
            foreach (var i in initial) states.Add(i);
            var mm = new StringBuilder("stateDiagram-v2\n"); var pu = new StringBuilder("@startuml\n");
            if (states.Contains("ANY")) { mm.Append("  state \"任一狀態\" as ANY\n"); pu.Append("state \"任一狀態\" as ANY\n"); }
            if (states.Contains("UNKNOWN")) { mm.Append("  state \"(運算結果)\" as UNKNOWN\n"); pu.Append("state \"(運算結果)\" as UNKNOWN\n"); }
            foreach (var i in initial) { mm.Append("  [*] --> " + i + "\n"); pu.Append("[*] --> " + i + "\n"); }
            foreach (var t in trans.OrderBy(x => x))
            {
                var p = t.Split('\t'); mm.Append("  " + p[0] + " --> " + p[1] + " : " + p[2] + "\n"); pu.Append(p[0] + " --> " + p[1] + " : " + p[2] + "\n");
            }
            pu.Append("@enduml\n");
            return new { error = (string)null, members = cands.Select(c => c.Name).ToList(), chosen = chosen.Name, mermaid = mm.ToString(), puml = pu.ToString(), transitions = trans.Count };
        }

        // ===== 資料流 =====
        class DF
        {
            public Dictionary<string, string> Nodes = new Dictionary<string, string>(); // id → "kind|label"
            public HashSet<string> Edges = new HashSet<string>();
            public int Count;
        }

        static string Nid(ISymbol s, DF df)
        {
            var fs = s as IFieldSymbol;
            if (fs != null && (fs.IsConst || (fs.ContainingType != null && fs.ContainingType.TypeKind == TypeKind.Enum))) return null; // 常數與列舉成員不是資料
            string kind = s is IParameterSymbol ? "param" : s is ILocalSymbol ? "local" : s is IFieldSymbol ? "field" : s is IPropertySymbol ? "prop" : null;
            if (kind == null) return null;
            var id = kind + "_" + s.Name; df.Nodes[id] = kind + "|" + s.Name; return id;
        }

        static IEnumerable<string> Sources(SemanticModel m, SyntaxNode expr, DF df)
        {
            foreach (var idn in expr.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (idn.Ancestors().TakeWhile(a => a != expr.Parent).Any(a => a is ArgumentSyntax && a != expr && a.Parent.Parent is InvocationExpressionSyntax)) continue;
                var s = m.GetSymbolInfo(idn).Symbol; if (s == null) continue;
                var id = Nid(s, df); if (id != null) yield return id;
            }
        }

        public static object DataFlow(Analyzer an, ISymbol method)
        {
            var body = FlowBuilder.BodyOf(method);
            if (body == null) return new { error = "這個成員沒有內文可分析。" };
            var m = ModelFor(an, body.SyntaxTree);
            var df = new DF(); var rows = new List<string>();
            Action<string, string, SyntaxNode> edge = (a, b, at) =>
            {
                if (a == null || b == null || a == b) return;
                var line = at.SyntaxTree.GetLineSpan(at.Span).StartLinePosition.Line + 1;
                if (df.Edges.Add(a + "\t" + b + "\tL" + line)) rows.Add(df.Nodes[a].Split('|')[1] + " → " + df.Nodes[b].Split('|')[1] + "(第 " + line + " 行)");
            };
            foreach (var p in (method as IMethodSymbol)?.Parameters ?? System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty) Nid(p, df);
            foreach (var node in body.DescendantNodes())
            {
                var vd = node as VariableDeclaratorSyntax;
                if (vd != null && vd.Initializer != null)
                {
                    var t = m.GetDeclaredSymbol(vd); var tid = t == null ? null : Nid(t, df);
                    foreach (var s in Sources(m, vd.Initializer.Value, df)) edge(s, tid, vd);
                    CallEdges(m, vd.Initializer.Value, tid, df, edge);
                    continue;
                }
                var asg = node as AssignmentExpressionSyntax;
                if (asg != null)
                {
                    var ls = m.GetSymbolInfo(asg.Left).Symbol; var tid = ls == null ? null : Nid(ls, df);
                    foreach (var s in Sources(m, asg.Right, df)) edge(s, tid, asg);
                    CallEdges(m, asg.Right, tid, df, edge);
                    continue;
                }
                var rt = node as ReturnStatementSyntax;
                if (rt != null && rt.Expression != null)
                {
                    df.Nodes["ret"] = "ret|回傳值";
                    foreach (var s in Sources(m, rt.Expression, df)) edge(s, "ret", rt);
                    CallEdges(m, rt.Expression, "ret", df, edge);
                }
                var inv = node as InvocationExpressionSyntax;
                if (inv != null && !(node.Parent is AssignmentExpressionSyntax) && !(node.Parent is EqualsValueClauseSyntax) && !(node.Parent is ReturnStatementSyntax))
                    CallEdges(m, inv, null, df, edge);
            }
            var mm = new StringBuilder("flowchart LR\n"); var pu = new StringBuilder("@startuml\n");
            foreach (var kv in df.Nodes)
            {
                var p = kv.Value.Split('|'); var label = Mm(p[1]);
                string shape = p[0] == "field" || p[0] == "prop" ? "[(\"" + label + "\")]" : p[0] == "param" ? "[/\"" + label + "\"/]" : p[0] == "call" ? "{{\"" + label + "\"}}" : p[0] == "ret" ? "([\"" + label + "\"])" : "(\"" + label + "\")";
                mm.Append("  " + kv.Key + shape + "\n");
                pu.Append((p[0] == "field" || p[0] == "prop" ? "database" : p[0] == "call" ? "usecase" : "rectangle") + " \"" + p[1] + "\" as " + kv.Key + "\n");
            }
            foreach (var e in df.Edges)
            {
                var p = e.Split('\t'); mm.Append("  " + p[0] + " -->|" + p[2] + "| " + p[1] + "\n"); pu.Append(p[0] + " --> " + p[1] + " : " + p[2] + "\n");
            }
            pu.Append("@enduml\n");
            return new { error = (string)null, mermaid = mm.ToString(), puml = pu.ToString(), rows, legend = "梯形=參數,圓柱=欄位/屬性,圓角=區域變數,六角=呼叫,體育場=回傳值;箭頭旁 L 數字是行號" };
        }

        static void CallEdges(SemanticModel m, SyntaxNode expr, string target, DF df, Action<string, string, SyntaxNode> edge)
        {
            foreach (var inv in expr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                var ms = m.GetSymbolInfo(inv).Symbol as IMethodSymbol; if (ms == null) continue;
                var cid = "call_" + ms.ContainingType.Name + "_" + ms.Name; df.Nodes[cid] = "call|" + ms.ContainingType.Name + "." + ms.Name + "()";
                var ma = inv.Expression as MemberAccessExpressionSyntax;
                if (ma != null) foreach (var s in Sources(m, ma.Expression, df)) edge(s, cid, inv);
                foreach (var a in inv.ArgumentList.Arguments)
                {
                    var outs = a.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
                    foreach (var s in Sources(m, a.Expression, df)) { if (outs) edge(cid, s, inv); else edge(s, cid, inv); }
                }
                if (target != null) edge(cid, target, inv);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    // ---------- 中介表示:先把方法內文轉成流程節點,再輸出成各種圖 ----------
    public abstract class FNode { public string Text; }
    public class FCall
    {
        public string TypeName, TypeKey, Method, Args; public bool External, Self, Create;
        public List<FNode> Expanded; // 展開被呼叫方法的內文
    }
    public class FAction : FNode { public List<FCall> Calls = new List<FCall>(); }
    public class FJump : FNode { public string Kind; } // return / throw / break / continue
    public class FIf : FNode { public List<FCall> CondCalls = new List<FCall>(); public List<FNode> Then = new List<FNode>(), Else = new List<FNode>(); }
    public class FLoop : FNode { public string Kind; public List<FCall> CondCalls = new List<FCall>(); public List<FNode> Body = new List<FNode>(); }
    public class FCase { public string Label; public List<FNode> Body = new List<FNode>(); }
    public class FSwitch : FNode { public List<FCall> CondCalls = new List<FCall>(); public List<FCase> Cases = new List<FCase>(); }
    public class FTry : FNode { public List<FNode> Body = new List<FNode>(), Finally = new List<FNode>(); public List<FCase> Catches = new List<FCase>(); }

    public class FlowOptions { public int Expand = 0; public bool External = false; public int CallDepth = 3; }

    /// <summary>把一個方法的語法樹轉成流程節點。</summary>
    public class FlowBuilder
    {
        readonly Analyzer an; readonly FlowOptions opt;
        readonly HashSet<string> asm;
        readonly Dictionary<SyntaxTree, SemanticModel> models = new Dictionary<SyntaxTree, SemanticModel>();

        public FlowBuilder(Analyzer an, FlowOptions opt)
        {
            this.an = an; this.opt = opt;
            asm = new HashSet<string>(an.Projects.Select(p => p.Name));
        }

        SemanticModel Model(SyntaxTree t)
        {
            SemanticModel m;
            if (models.TryGetValue(t, out m)) return m;
            foreach (var p in an.Projects)
                if (p.Compilation.SyntaxTrees.Contains(t)) { m = p.Compilation.GetSemanticModel(t); break; }
            models[t] = m; return m;
        }

        public static SyntaxNode BodyOf(ISymbol s)
        {
            foreach (var r in s.DeclaringSyntaxReferences)
            {
                var n = r.GetSyntax();
                var md = n as BaseMethodDeclarationSyntax;
                if (md != null) return (SyntaxNode)md.Body ?? md.ExpressionBody;
                var pd = n as PropertyDeclarationSyntax;
                if (pd != null) return pd.ExpressionBody ?? (SyntaxNode)pd.AccessorList;
                var ld = n as LocalFunctionStatementSyntax;
                if (ld != null) return (SyntaxNode)ld.Body ?? ld.ExpressionBody;
            }
            return null;
        }

        public List<FNode> Build(ISymbol method, int depth, HashSet<string> stack)
        {
            var res = new List<FNode>();
            var body = BodyOf(method);
            if (body == null) return res;
            var model = Model(body.SyntaxTree);
            if (model == null) return res;
            stack.Add(Analyzer.Key(method));
            var owner = method.ContainingType;
            if (body is BlockSyntax) res.AddRange(Stmts(((BlockSyntax)body).Statements, model, owner, depth, stack));
            else if (body is ArrowExpressionClauseSyntax)
            {
                var a = new FAction { Text = "return " + Short(((ArrowExpressionClauseSyntax)body).Expression) };
                a.Calls.AddRange(Calls(((ArrowExpressionClauseSyntax)body).Expression, model, owner, depth, stack)); res.Add(a);
            }
            else if (body is AccessorListSyntax)
                foreach (var ac in ((AccessorListSyntax)body).Accessors)
                    if (ac.Body != null) res.AddRange(Stmts(ac.Body.Statements, model, owner, depth, stack));
            stack.Remove(Analyzer.Key(method));
            return res;
        }

        static string Short(SyntaxNode n, int max = 70)
        {
            var s = string.Join(" ", n.ToString().Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            return s.Length > max ? s.Substring(0, max - 1) + "…" : s;
        }

        List<FNode> Stmts(IEnumerable<StatementSyntax> list, SemanticModel m, INamedTypeSymbol owner, int d, HashSet<string> st)
        {
            var res = new List<FNode>();
            foreach (var s in list) res.AddRange(Stmt(s, m, owner, d, st));
            return res;
        }

        List<FNode> Block(StatementSyntax s, SemanticModel m, INamedTypeSymbol owner, int d, HashSet<string> st)
        {
            if (s == null) return new List<FNode>();
            var b = s as BlockSyntax;
            return b != null ? Stmts(b.Statements, m, owner, d, st) : Stmt(s, m, owner, d, st);
        }

        List<FNode> Stmt(StatementSyntax s, SemanticModel m, INamedTypeSymbol owner, int d, HashSet<string> st)
        {
            var res = new List<FNode>();
            switch (s)
            {
                case BlockSyntax b: return Stmts(b.Statements, m, owner, d, st);
                case IfStatementSyntax i:
                    {
                        var f = new FIf { Text = Short(i.Condition) };
                        f.CondCalls.AddRange(Calls(i.Condition, m, owner, d, st));
                        f.Then = Block(i.Statement, m, owner, d, st);
                        if (i.Else != null) f.Else = Block(i.Else.Statement, m, owner, d, st);
                        res.Add(f); break;
                    }
                case WhileStatementSyntax w: res.Add(Loop("while", w.Condition, w.Statement, m, owner, d, st)); break;
                case DoStatementSyntax dw: res.Add(Loop("do", dw.Condition, dw.Statement, m, owner, d, st)); break;
                case ForStatementSyntax fo:
                    {
                        var l = Loop("for", fo.Condition, fo.Statement, m, owner, d, st);
                        if (fo.Condition == null) l.Text = "for(;;)";
                        res.Add(l); break;
                    }
                case ForEachStatementSyntax fe:
                    {
                        var l = Loop("foreach", fe.Expression, fe.Statement, m, owner, d, st);
                        l.Text = fe.Identifier.Text + " in " + Short(fe.Expression, 40); res.Add(l); break;
                    }
                case SwitchStatementSyntax sw:
                    {
                        var f = new FSwitch { Text = Short(sw.Expression) };
                        f.CondCalls.AddRange(Calls(sw.Expression, m, owner, d, st));
                        foreach (var sec in sw.Sections)
                        {
                            var c = new FCase { Label = string.Join(", ", sec.Labels.Select(x => x is DefaultSwitchLabelSyntax ? "default" : Short(x, 30).Replace("case ", "").TrimEnd(':'))) };
                            c.Body = Stmts(sec.Statements, m, owner, d, st);
                            f.Cases.Add(c);
                        }
                        res.Add(f); break;
                    }
                case TryStatementSyntax t:
                    {
                        var f = new FTry { Text = "try" };
                        f.Body = Block(t.Block, m, owner, d, st);
                        foreach (var c in t.Catches)
                            f.Catches.Add(new FCase { Label = c.Declaration == null ? "例外" : c.Declaration.Type.ToString(), Body = Block(c.Block, m, owner, d, st) });
                        if (t.Finally != null) f.Finally = Block(t.Finally.Block, m, owner, d, st);
                        res.Add(f); break;
                    }
                case UsingStatementSyntax u: res.AddRange(Block(u.Statement, m, owner, d, st)); break;
                case LockStatementSyntax lk: res.AddRange(Block(lk.Statement, m, owner, d, st)); break;
                case ReturnStatementSyntax r:
                    {
                        var a = new FAction { Text = r.ToString().TrimEnd(';') };
                        if (r.Expression != null) a.Calls.AddRange(Calls(r.Expression, m, owner, d, st));
                        if (a.Calls.Count > 0) res.Add(a);
                        res.Add(new FJump { Kind = "return", Text = Short(r).TrimEnd(';') }); break;
                    }
                case ThrowStatementSyntax th:
                    {
                        var a = new FAction { Text = Short(th) };
                        if (th.Expression != null) a.Calls.AddRange(Calls(th.Expression, m, owner, d, st));
                        if (a.Calls.Count > 0) res.Add(a);
                        res.Add(new FJump { Kind = "throw", Text = Short(th).TrimEnd(';') }); break;
                    }
                case BreakStatementSyntax _: res.Add(new FJump { Kind = "break", Text = "break" }); break;
                case ContinueStatementSyntax _: res.Add(new FJump { Kind = "continue", Text = "continue" }); break;
                case LocalFunctionStatementSyntax _: break;
                default:
                    {
                        var a = new FAction { Text = Short(s).TrimEnd(';') };
                        a.Calls.AddRange(Calls(s, m, owner, d, st));
                        res.Add(a); break;
                    }
            }
            return res;
        }

        FLoop Loop(string kind, SyntaxNode cond, StatementSyntax body, SemanticModel m, INamedTypeSymbol owner, int d, HashSet<string> st)
        {
            var l = new FLoop { Kind = kind, Text = cond == null ? "" : Short(cond) };
            if (cond != null) l.CondCalls.AddRange(Calls(cond, m, owner, d, st));
            l.Body = Block(body, m, owner, d, st);
            return l;
        }

        /// <summary>依執行順序取出一段語法內的方法呼叫(內層先、外層後);略過 lambda 內部。</summary>
        List<FCall> Calls(SyntaxNode n, SemanticModel m, INamedTypeSymbol owner, int d, HashSet<string> st)
        {
            var res = new List<FCall>();
            var nodes = n.DescendantNodesAndSelf(x => !(x is AnonymousFunctionExpressionSyntax) && !(x is LocalFunctionStatementSyntax))
                .Where(x => x is InvocationExpressionSyntax || x is BaseObjectCreationExpressionSyntax)
                .OrderBy(x => x.Span.End).ThenByDescending(x => x.Span.Start).ToList();
            foreach (var x in nodes)
            {
                var ms = m.GetSymbolInfo(x).Symbol as IMethodSymbol;
                if (ms == null || ms.ContainingType == null) continue;
                bool ext = ms.ContainingAssembly == null || !asm.Contains(ms.ContainingAssembly.Name);
                if (ext && !opt.External) continue;
                var args = x is InvocationExpressionSyntax ? ((InvocationExpressionSyntax)x).ArgumentList
                    : ((BaseObjectCreationExpressionSyntax)x).ArgumentList;
                var argText = args == null ? "" : string.Join(", ", args.Arguments.Select(a => Short(a.Expression, 18)));
                if (argText.Length > 40) argText = argText.Substring(0, 39) + "…";
                var c = new FCall
                {
                    TypeName = ms.ContainingType.Name, TypeKey = Analyzer.Key(ms.ContainingType), External = ext,
                    Create = ms.MethodKind == MethodKind.Constructor, Args = argText,
                    Method = ms.MethodKind == MethodKind.Constructor ? "new" : ms.Name,
                    Self = SymbolEqualityComparer.Default.Equals(ms.ContainingType, owner)
                };
                if (!ext && d > 0 && !c.Create && !st.Contains(Analyzer.Key(ms)))
                {
                    var ex = Build(ms, d - 1, st);
                    if (ex.Count > 0) c.Expanded = ex;
                }
                res.Add(c);
            }
            return res;
        }
    }

    // ---------- 輸出 ----------
    public static class FlowWriter
    {
        static bool HasCalls(IEnumerable<FNode> ns)
        {
            foreach (var n in ns)
            {
                if (n is FAction && ((FAction)n).Calls.Count > 0) return true;
                var i = n as FIf; if (i != null && (i.CondCalls.Count > 0 || HasCalls(i.Then) || HasCalls(i.Else))) return true;
                var l = n as FLoop; if (l != null && (l.CondCalls.Count > 0 || HasCalls(l.Body))) return true;
                var s = n as FSwitch; if (s != null && (s.CondCalls.Count > 0 || s.Cases.Any(c => HasCalls(c.Body)))) return true;
                var t = n as FTry; if (t != null && (HasCalls(t.Body) || t.Catches.Any(c => HasCalls(c.Body)) || HasCalls(t.Finally))) return true;
            }
            return false;
        }

        static string MmText(string s) { return (s ?? "").Replace(";", ",").Replace("#", "＃").Replace("\"", "'").Replace("%%", "％"); }
        static string PuText(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        // ===== 循序圖 =====
        class Seq
        {
            public bool Puml; public StringBuilder Sb = new StringBuilder();
            public Dictionary<string, string> Ids = new Dictionary<string, string>(); public List<string> Order = new List<string>();
            public string Id(string key, string name) { string v; if (!Ids.TryGetValue(key, out v)) { v = "P" + (Ids.Count + 1); Ids[key] = v; Order.Add(key + "\t" + name); } return v; }
            public void L(int ind, string s) { Sb.Append(new string(' ', ind * 2)).Append(s).Append('\n'); }
            string T(string s) { return Puml ? PuText(s) : MmText(s); }

            public void Nodes(List<FNode> ns, string cur, int ind)
            {
                foreach (var n in ns)
                {
                    var a = n as FAction; if (a != null) { foreach (var c in a.Calls) Call(c, cur, ind); continue; }
                    var i = n as FIf;
                    if (i != null)
                    {
                        foreach (var c in i.CondCalls) Call(c, cur, ind);
                        if (!HasCalls(i.Then) && !HasCalls(i.Else)) continue;
                        IfChain(i, cur, ind, true); continue;
                    }
                    var l = n as FLoop;
                    if (l != null)
                    {
                        if (!HasCalls(l.Body) && l.CondCalls.Count == 0) continue;
                        L(ind, "loop " + T(l.Kind + " " + l.Text));
                        foreach (var c in l.CondCalls) Call(c, cur, ind + 1);
                        Nodes(l.Body, cur, ind + 1); L(ind, "end"); continue;
                    }
                    var s = n as FSwitch;
                    if (s != null)
                    {
                        foreach (var c in s.CondCalls) Call(c, cur, ind);
                        if (!s.Cases.Any(c => HasCalls(c.Body))) continue;
                        for (int k = 0; k < s.Cases.Count; k++)
                        {
                            L(ind, (k == 0 ? "alt " : "else ") + T("switch " + s.Text + " = " + s.Cases[k].Label));
                            Nodes(s.Cases[k].Body, cur, ind + 1);
                        }
                        L(ind, "end"); continue;
                    }
                    var t = n as FTry;
                    if (t != null)
                    {
                        if (!HasCalls(new[] { t })) continue;
                        if (Puml)
                        {
                            L(ind, "group try"); Nodes(t.Body, cur, ind + 1);
                            foreach (var c in t.Catches) { L(ind, "else catch " + T(c.Label)); Nodes(c.Body, cur, ind + 1); }
                            L(ind, "end");
                        }
                        else
                        {
                            L(ind, "critical try"); Nodes(t.Body, cur, ind + 1);
                            foreach (var c in t.Catches) { L(ind, "option catch " + T(c.Label)); Nodes(c.Body, cur, ind + 1); }
                            L(ind, "end");
                        }
                        if (HasCalls(t.Finally)) { L(ind, Puml ? "group finally" : "rect rgba(128,128,128,0.15)"); Nodes(t.Finally, cur, ind + 1); L(ind, "end"); }
                    }
                }
            }

            void IfChain(FIf i, string cur, int ind, bool first)
            {
                bool hasElse = HasCalls(i.Else) || i.Else.Count > 0 && HasCalls(i.Else);
                var elseIsIf = i.Else.Count == 1 && i.Else[0] is FIf;
                if (!HasCalls(i.Else)) { L(ind, "opt " + T(i.Text)); Nodes(i.Then, cur, ind + 1); L(ind, "end"); return; }
                L(ind, "alt " + T(i.Text)); Nodes(i.Then, cur, ind + 1);
                var e = i;
                while (true)
                {
                    var next = e.Else.Count == 1 ? e.Else[0] as FIf : null;
                    if (next == null) { L(ind, "else 否則"); Nodes(e.Else, cur, ind + 1); break; }
                    foreach (var c in next.CondCalls) { /* 條件中的呼叫併入該分支開頭 */ }
                    L(ind, "else " + T(next.Text)); foreach (var c in next.CondCalls) Call(c, cur, ind + 1);
                    Nodes(next.Then, cur, ind + 1); e = next;
                }
                L(ind, "end");
            }

            void Call(FCall c, string cur, int ind)
            {
                var to = Id(c.TypeKey, c.TypeName);
                var msg = T(c.Create ? "new " + c.TypeName + "(" + c.Args + ")" : c.Method + "(" + c.Args + ")");
                if (Puml) L(ind, cur + " -> " + to + " : " + msg); else L(ind, cur + "->>" + to + ": " + msg);
                if (c.Expanded != null && c.Expanded.Count > 0 && HasCalls(c.Expanded))
                {
                    L(ind, "activate " + to);
                    Nodes(c.Expanded, to, ind + 1);
                    if (Puml) L(ind, to + " --> " + cur + " : 回傳"); else L(ind, to + "-->>" + cur + ": 回傳");
                    L(ind, "deactivate " + to);
                }
            }
        }

        public static string Sequence(ISymbol method, List<FNode> flow, bool puml)
        {
            var q = new Seq { Puml = puml };
            var owner = method.ContainingType;
            var caller = q.Id("#caller", "呼叫者");
            var self = q.Id(Analyzer.Key(owner), owner.Name);
            var ps = method is IMethodSymbol ? string.Join(", ", ((IMethodSymbol)method).Parameters.Select(p => p.Name)) : "";
            var name = method.MethodKind() == MethodKind.Constructor ? "new" : method.Name;
            if (puml) q.L(0, caller + " -> " + self + " : " + PuText(name + "(" + ps + ")")); else q.L(0, caller + "->>" + self + ": " + MmText(name + "(" + ps + ")"));
            q.L(0, "activate " + self);
            q.Nodes(flow, self, 0);
            q.L(0, "deactivate " + self);
            var sb = new StringBuilder();
            sb.Append(puml ? "@startuml\nautonumber\n" : "sequenceDiagram\n  autonumber\n");
            foreach (var o in q.Order)
            {
                var kv = o.Split('\t'); var id = q.Ids[kv[0]];
                if (puml) sb.Append(kv[0] == "#caller" ? "actor " : "participant ").Append("\"" + kv[1] + "\" as " + id + "\n");
                else sb.Append("  " + (kv[0] == "#caller" ? "actor " : "participant ") + id + " as " + MmText(kv[1]) + "\n");
            }
            foreach (var line in q.Sb.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) sb.Append(puml ? line : "  " + line).Append('\n');
            if (puml) sb.Append("@enduml\n");
            return sb.ToString();
        }

        // ===== 活動圖:Mermaid 流程圖 / PlantUML 活動圖 =====
        class Flow
        {
            public StringBuilder Sb = new StringBuilder(); int n;
            public List<KeyValuePair<string, string>> Open = new List<KeyValuePair<string, string>>();
            public List<string> Ends = new List<string>();
            public Stack<List<KeyValuePair<string, string>>> Breaks = new Stack<List<KeyValuePair<string, string>>>();
            public Stack<string> Heads = new Stack<string>();
            string NewId() { return "N" + (++n); }
            // 同時記錄結構化的節點與連線,給流程畫布使用
            public List<object> GN = new List<object>(), GE = new List<object>();
            void Edge(string from, string label, string to, bool back = false)
            {
                Sb.Append("  " + from + (label == null ? " --> " : " -->|" + label + "| ") + to + "\n");
                GE.Add(new { from, to, label, back });
            }
            static void Rec(List<object> gn, string id, string def)
            {
                var a = def.IndexOf('"'); var b = def.LastIndexOf('"');
                var text = a >= 0 && b > a ? def.Substring(a + 1, b - a - 1) : "";
                gn.Add(new { id, text, shape = def.StartsWith("([") ? "end" : def.StartsWith("{") ? "decision" : "action" });
            }
            string Node(string def) { var id = NewId(); Sb.Append("  " + id + def + "\n"); Rec(GN, id, def); return id; }
            void Connect(string to) { foreach (var o in Open) Edge(o.Key, o.Value, to); Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(to, null) }; }
            static string Q(string s) { return "\"" + MmText(s) + "\""; }

            public void Start() { var id = "S0"; Sb.Append("  S0([\"開始\"])\n"); GN.Add(new { id = "S0", text = "開始", shape = "start" }); Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(id, null) }; }
            public void Finish()
            {
                Sb.Append("  E0([\"結束\"])\n"); GN.Add(new { id = "E0", text = "結束", shape = "terminal" });
                foreach (var o in Open) Edge(o.Key, o.Value, "E0");
                foreach (var r in Ends) Edge(r, null, "E0");
            }

            public void Nodes(List<FNode> ns)
            {
                foreach (var x in ns)
                {
                    if (Open.Count == 0) return; // 前面已 return,後面不可達
                    var a = x as FAction;
                    if (a != null) { var id = NodeDef("[" + Q(a.Text) + "]"); Connect(id); continue; }
                    var j = x as FJump;
                    if (j != null)
                    {
                        if (j.Kind == "return" || j.Kind == "throw") { var id = NodeDef("([" + Q(j.Text) + "])"); Connect(id); Ends.Add(id); Open = new List<KeyValuePair<string, string>>(); }
                        else if (j.Kind == "break" && Breaks.Count > 0) { Breaks.Peek().AddRange(Open); Open = new List<KeyValuePair<string, string>>(); }
                        else if (j.Kind == "continue" && Heads.Count > 0) { foreach (var o in Open) Edge(o.Key, o.Value, Heads.Peek(), true); Open = new List<KeyValuePair<string, string>>(); }
                        continue;
                    }
                    var i = x as FIf;
                    if (i != null)
                    {
                        var d = NodeDef("{" + Q(i.Text + "?") + "}"); Connect(d);
                        Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(d, "是") }; Nodes(i.Then);
                        var thenOpen = Open;
                        Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(d, "否") }; Nodes(i.Else);
                        Open = thenOpen.Concat(Open).ToList(); continue;
                    }
                    var l = x as FLoop;
                    if (l != null)
                    {
                        var h = NodeDef("{" + Q(l.Kind + " " + l.Text) + "}"); Connect(h);
                        Breaks.Push(new List<KeyValuePair<string, string>>()); Heads.Push(h);
                        Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(h, "是") }; Nodes(l.Body);
                        foreach (var o in Open) Edge(o.Key, o.Value, h, true);
                        Heads.Pop(); var br = Breaks.Pop();
                        Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(h, "否") }; Open.AddRange(br); continue;
                    }
                    var s = x as FSwitch;
                    if (s != null)
                    {
                        var d = NodeDef("{" + Q("switch " + s.Text) + "}"); Connect(d);
                        var merged = new List<KeyValuePair<string, string>>();
                        Breaks.Push(new List<KeyValuePair<string, string>>());
                        foreach (var c in s.Cases)
                        {
                            Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(d, MmText(c.Label)) }; Nodes(c.Body); merged.AddRange(Open);
                        }
                        merged.AddRange(Breaks.Pop()); Open = merged; continue;
                    }
                    var t = x as FTry;
                    if (t != null)
                    {
                        var tid = NodeDef("[" + Q("try") + "]"); Connect(tid);
                        Nodes(t.Body); var ends = new List<KeyValuePair<string, string>>(Open);
                        foreach (var c in t.Catches)
                        {
                            Open = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(tid, "例外 " + MmText(c.Label)) }; Nodes(c.Body); ends.AddRange(Open);
                        }
                        Open = ends;
                        if (t.Finally.Count > 0) Nodes(t.Finally);
                    }
                }
            }
            string NodeDef(string def) { return Node(def); }
        }

        /// <summary>活動圖的結構化資料(節點與連線),供流程畫布手動排版。back=true 的是迴圈回頭線。</summary>
        public static object ActivityGraph(List<FNode> flow)
        {
            var f = new Flow(); f.Sb.Append("flowchart TD\n"); f.Start(); f.Nodes(flow); f.Finish();
            return new { nodes = f.GN, edges = f.GE };
        }

        public static string ActivityMermaid(List<FNode> flow)
        {
            var f = new Flow(); f.Sb.Append("flowchart TD\n"); f.Start(); f.Nodes(flow); f.Finish();
            return f.Sb.ToString();
        }

        static void Act(StringBuilder sb, List<FNode> ns, int ind)
        {
            Func<string, string> pad = s => new string(' ', ind * 2) + s + "\n";
            Func<string, string> act = s => PuText(s).Replace(";", "；");
            foreach (var x in ns)
            {
                var a = x as FAction; if (a != null) { sb.Append(pad(":" + act(a.Text) + ";")); continue; }
                var j = x as FJump;
                if (j != null)
                {
                    if (j.Kind == "return" || j.Kind == "throw") { sb.Append(pad(":" + act(j.Text) + ";")); sb.Append(pad("stop")); }
                    else if (j.Kind == "break") sb.Append(pad("break"));
                    else sb.Append(pad(":continue;"));
                    continue;
                }
                var i = x as FIf;
                if (i != null)
                {
                    sb.Append(pad("if (" + act(i.Text) + "?) then (是)")); Act(sb, i.Then, ind + 1);
                    if (i.Else.Count > 0) { sb.Append(pad("else (否)")); Act(sb, i.Else, ind + 1); }
                    sb.Append(pad("endif")); continue;
                }
                var l = x as FLoop;
                if (l != null)
                {
                    if (l.Kind == "do") { sb.Append(pad("repeat")); Act(sb, l.Body, ind + 1); sb.Append(pad("repeat while (" + act(l.Text) + "?) is (是)")); }
                    else { sb.Append(pad("while (" + act(l.Kind + " " + l.Text) + ") is (是)")); Act(sb, l.Body, ind + 1); sb.Append(pad("endwhile (否)")); }
                    continue;
                }
                var s = x as FSwitch;
                if (s != null)
                {
                    sb.Append(pad("switch (" + act(s.Text) + ")"));
                    foreach (var c in s.Cases) { sb.Append(pad("case (" + act(c.Label) + ")")); Act(sb, c.Body.Where(z => !(z is FJump && ((FJump)z).Kind == "break")).ToList(), ind + 1); }
                    sb.Append(pad("endswitch")); continue;
                }
                var t = x as FTry;
                if (t != null)
                {
                    sb.Append(pad("partition \"try\" {")); Act(sb, t.Body, ind + 1); sb.Append(pad("}"));
                    foreach (var c in t.Catches) { sb.Append(pad("partition \"catch " + act(c.Label) + "\" {")); Act(sb, c.Body, ind + 1); sb.Append(pad("}")); }
                    if (t.Finally.Count > 0) { sb.Append(pad("partition \"finally\" {")); Act(sb, t.Finally, ind + 1); sb.Append(pad("}")); }
                }
            }
        }

        public static string ActivityPuml(List<FNode> flow)
        {
            var sb = new StringBuilder("@startuml\nstart\n"); Act(sb, flow, 0);
            var last = flow.LastOrDefault() as FJump; // 最後一句已是 return/throw 時,Act 已輸出 stop
            if (last == null || (last.Kind != "return" && last.Kind != "throw")) sb.Append("stop\n");
            sb.Append("@enduml\n"); return sb.ToString();
        }
    }

    static class SymExt
    {
        public static MethodKind MethodKind(this ISymbol s) { var m = s as IMethodSymbol; return m == null ? Microsoft.CodeAnalysis.MethodKind.Ordinary : m.MethodKind; }
    }
}

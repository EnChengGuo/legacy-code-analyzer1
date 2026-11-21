using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    /// <summary>
    /// God Class 解剖:欄位清冊、儲存對照(欄位 ↔ 儲存鍵)、UI 對照(控制項 ↔ 資料欄位)、職責分群。
    /// 儲存對照與 UI 對照是依程式寫法推測(字串鍵、屬性標註、事件掛接、賦值方向),請當作線索而非定論。
    /// </summary>
    public static class GodClass
    {
        static readonly HashSet<string> ControlBases = new HashSet<string> {
            "System.Windows.Forms.Control", "System.Windows.Forms.ToolStripItem", "System.Windows.UIElement", "System.ComponentModel.Component" };
        static readonly HashSet<string> Stop = new HashSet<string> { "get", "set", "on", "is", "the", "to", "of", "and", "init", "do", "handle", "click",
            "changed", "event", "form", "button", "btn", "value", "new", "has", "can", "check", "load", "from", "all", "for", "with", "by" };

        static bool IsControl(ITypeSymbol t)
        {
            for (var b = t; b != null; b = b.BaseType) if (ControlBases.Contains(b.ToDisplayString())) return true;
            return false;
        }

        static SemanticModel ModelFor(Analyzer an, SyntaxTree t)
        {
            foreach (var p in an.Projects) if (p.Compilation.SyntaxTrees.Contains(t)) return p.Compilation.GetSemanticModel(t);
            return null;
        }

        static string Short(ISymbol s) { return s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat); }
        static string Sign(ISymbol s) { return s.DeclaredAccessibility == Accessibility.Public ? "+" : s.DeclaredAccessibility == Accessibility.Private ? "-" : s.DeclaredAccessibility == Accessibility.Protected ? "#" : "~"; }

        static List<string> Words(string name)
        {
            return Regex.Matches(name.Replace('_', ' '), "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+|[\\u4e00-\\u9fff]+").Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant()).Where(w => w.Length >= 2 && !Stop.Contains(w)).ToList();
        }

        public static object Analyze(Analyzer an, SymbolInfoItem item, double hub)
        {
            var T = (INamedTypeSymbol)item.Symbol; var tkey = item.Key;
            var mm = Metrics.Members(an).ToDictionary(m => m.Item.Key);
            var refs = an.AllRefs();
            var data = T.GetMembers().Where(m => !m.IsImplicitlyDeclared && (m is IFieldSymbol || m is IPropertySymbol)).ToList();
            var dataKeys = new HashSet<string>(data.Select(Analyzer.Key));
            var units = T.GetMembers().Where(m => !m.IsImplicitlyDeclared && ((m is IMethodSymbol && ((IMethodSymbol)m).AssociatedSymbol == null) || m is IPropertySymbol)
                && FlowBuilder.BodyOf(m) != null).ToList();
            var unitKeys = new HashSet<string>(units.Select(Analyzer.Key));
            int nUnits = Math.Max(1, units.Count);

            var byTarget = refs.Where(r => dataKeys.Contains(r.TargetKey) || unitKeys.Contains(r.TargetKey)).GroupBy(r => r.TargetKey).ToDictionary(g => g.Key, g => g.ToList());
            Func<string, List<Ref>> R = k => byTarget.ContainsKey(k) ? byTarget[k] : new List<Ref>();

            // ===== 欄位清冊 =====
            var inventory = new List<KeyValuePair<int, object>>(); var usersOf = new Dictionary<string, HashSet<string>>();
            foreach (var m in data)
            {
                var k = Analyzer.Key(m); var rs = R(k);
                var inside = rs.Where(r => r.EnclosingTypeKey == tkey && r.EnclosingKey != k).ToList();
                var outside = rs.Where(r => r.EnclosingTypeKey != tkey).ToList();
                var users = new HashSet<string>(inside.Select(r => r.EnclosingKey)); usersOf[k] = users;
                int readers = rs.Where(r => r.Kind == "讀" || r.Kind == "讀寫").Select(r => r.EnclosingKey).Distinct().Count();
                int writers = rs.Where(r => r.Kind == "寫" || r.Kind == "讀寫").Select(r => r.EnclosingKey).Distinct().Count();
                var flags = new List<string>();
                if (inside.Count == 0 && outside.Count == 0) flags.Add("未使用");
                else if (writers > 0 && readers == 0) flags.Add("只寫不讀");
                if (users.Count == 1 && outside.Count == 0) flags.Add("只被一個成員使用(可能該是區域變數)");
                if ((double)users.Count / nUnits > hub) flags.Add("樞紐(被 " + users.Count + " 個成員共用)");
                var f = m as IFieldSymbol; var loc = m.Locations.FirstOrDefault(l => l.IsInSource);
                inventory.Add(new KeyValuePair<int, object>(inside.Count + outside.Count, new
                {
                    name = m.Name, kind = m is IFieldSymbol ? "欄位" : "屬性", type = m is IFieldSymbol ? Short(((IFieldSymbol)m).Type) : Short(((IPropertySymbol)m).Type),
                    access = Sign(m), isStatic = m.IsStatic, isReadonly = f != null && (f.IsReadOnly || f.IsConst), line = loc == null ? 0 : loc.GetLineSpan().StartLinePosition.Line + 1,
                    readers, writers, refsInside = inside.Count, refsOutside = outside.Count, usedBy = users.Count, flags
                }));
            }

            // ===== 職責分群 =====
            var parent = units.ToDictionary(Analyzer.Key, Analyzer.Key);
            Func<string, string> find = null; find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
            Action<string, string> union = (a, b) => { a = find(a); b = find(b); if (a != b) parent[a] = b; };
            var hubFields = new HashSet<string>(data.Select(Analyzer.Key).Where(k => (double)usersOf[k].Count / nUnits > hub));
            foreach (var m in data)
            {
                var k = Analyzer.Key(m); if (hubFields.Contains(k)) continue;
                var us = usersOf[k].Where(unitKeys.Contains).ToList();
                for (int i = 1; i < us.Count; i++) union(us[0], us[i]);
            }
            var callIn = new Dictionary<string, int>();
            foreach (var r in refs.Where(r => r.EnclosingTypeKey == tkey && unitKeys.Contains(r.TargetKey) && unitKeys.Contains(r.EnclosingKey) && r.TargetKey != r.EnclosingKey))
                callIn[r.TargetKey] = (callIn.ContainsKey(r.TargetKey) ? callIn[r.TargetKey] : 0) + 1;
            foreach (var r in refs.Where(r => r.EnclosingTypeKey == tkey && unitKeys.Contains(r.TargetKey) && unitKeys.Contains(r.EnclosingKey) && r.TargetKey != r.EnclosingKey))
                if ((double)(callIn[r.TargetKey]) / nUnits <= hub) union(r.EnclosingKey, r.TargetKey); // 被太多方法呼叫的(例如寫記錄)不拿來連結
            var comps = units.GroupBy(u => find(Analyzer.Key(u))).Select(g => g.ToList()).OrderByDescending(g => g.Count).ToList();
            int lcom4 = comps.Count;
            var singles = comps.Where(c => c.Count == 1 && !data.Any(d => !hubFields.Contains(Analyzer.Key(d)) && usersOf[Analyzer.Key(d)].Contains(Analyzer.Key(c[0])))).ToList();
            var clusters = new List<object>();
            Func<List<ISymbol>, string, object> mk = (list, forced) =>
            {
                var keys = new HashSet<string>(list.Select(Analyzer.Key));
                var words = list.SelectMany(x => Words(x.Name)).GroupBy(w => w).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(3).Select(g => g.Key).ToList();
                var own = data.Where(d => !hubFields.Contains(Analyzer.Key(d)) && usersOf[Analyzer.Key(d)].Count > 0 && usersOf[Analyzer.Key(d)].All(keys.Contains)).Select(d => d.Name).ToList();
                var touchedHubs = data.Where(d => hubFields.Contains(Analyzer.Key(d)) && usersOf[Analyzer.Key(d)].Any(keys.Contains)).Select(d => d.Name).ToList();
                int loc = 0, cc = 0; foreach (var x in list) { MemberMetric mt; if (mm.TryGetValue(Analyzer.Key(x), out mt)) { loc += mt.Loc; cc += mt.CC; } }
                return new
                {
                    label = forced ?? (words.Count > 0 ? "「" + string.Join(" / ", words.Take(2)) + "」相關" : "(無法命名)"),
                    methods = list.Select(x => { MemberMetric mt; mm.TryGetValue(Analyzer.Key(x), out mt); return new { name = x.Name, line = (x.Locations.FirstOrDefault(l => l.IsInSource) == null ? 0 : x.Locations.First(l => l.IsInSource).GetLineSpan().StartLinePosition.Line + 1), loc = mt == null ? 0 : mt.Loc, cc = mt == null ? 0 : mt.CC }; }).OrderBy(x => x.line).ToList(),
                    count = list.Count, loc, cc, ownFields = own, hubFields = touchedHubs
                };
            };
            foreach (var c in comps.Where(c => !singles.Contains(c))) clusters.Add(mk(c, null));
            if (singles.Count > 0) clusters.Add(mk(singles.SelectMany(s => s).ToList(), "各自獨立的方法(沒有共用資料、也沒有互相呼叫)"));

            // ===== 儲存對照 =====
            var storage = new List<object>(); var evid = new Dictionary<string, List<string>>();
            Action<string, string> addEv = (k, s) => { List<string> l; if (!evid.TryGetValue(k, out l)) evid[k] = l = new List<string>(); if (!l.Contains(s) && l.Count < 6) l.Add(s); };
            foreach (var m in data)
                foreach (var a in m.GetAttributes())
                {
                    var an2 = a.AttributeClass == null ? "" : a.AttributeClass.Name.Replace("Attribute", "");
                    if (Regex.IsMatch(an2, "^(Xml|DataMember|JsonProperty|Column|Key|Required|DefaultValue|UserScopedSetting|ApplicationScopedSetting|Field|BsonElement|ProtoMember|Serializable|NonSerialized|Persist|Config|Setting)", RegexOptions.IgnoreCase))
                        addEv(Analyzer.Key(m), "屬性標註 [" + an2 + (a.ConstructorArguments.Length > 0 ? "(" + string.Join(", ", a.ConstructorArguments.Select(x => x.Value)) + ")" : "") + "]");
                }
            var literalsByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ctrls = data.Where(m => IsControl(m is IFieldSymbol ? ((IFieldSymbol)m).Type : ((IPropertySymbol)m).Type)).ToList();
            var ctrlKeys = new HashSet<string>(ctrls.Select(Analyzer.Key));
            var dataOnly = data.Where(d => !ctrlKeys.Contains(Analyzer.Key(d))).ToDictionary(Analyzer.Key);
            var uiRows = new List<object>(); var wired = new Dictionary<string, List<object>>(); var seenPair = new HashSet<string>();

            foreach (var tree in T.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Distinct())
            {
                var model = ModelFor(an, tree); if (model == null) continue;
                var typeNodes = T.DeclaringSyntaxReferences.Where(r => r.SyntaxTree == tree).Select(r => r.GetSyntax()).ToList();
                foreach (var tn in typeNodes)
                    foreach (var n in tn.DescendantNodes())
                    {
                        int line = tree.GetLineSpan(n.Span).StartLinePosition.Line + 1;
                        // --- 儲存鍵:字串字面值當索引或參數 ---
                        var inv = n as InvocationExpressionSyntax; var ea = n as ElementAccessExpressionSyntax;
                        IEnumerable<ExpressionSyntax> argExprs = inv != null ? inv.ArgumentList.Arguments.Select(a => a.Expression)
                            : ea != null ? ea.ArgumentList.Arguments.Select(a => a.Expression) : null;
                        if (argExprs != null)
                        {
                            var args = argExprs.ToList();
                            var lit = args.OfType<LiteralExpressionSyntax>().FirstOrDefault(l => l.IsKind(SyntaxKind.StringLiteralExpression));
                            if (lit != null && lit.Token.ValueText.Length > 0 && lit.Token.ValueText.Length <= 60)
                            {
                                var key = lit.Token.ValueText; var how = inv != null ? (inv.Expression is MemberAccessExpressionSyntax ? ((MemberAccessExpressionSyntax)inv.Expression).Name.Identifier.Text : inv.Expression.ToString()) + "(…)" : "索引子 [..]";
                                foreach (var other in args.Where(x => x != lit))
                                    foreach (var id in other.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                                    {
                                        var s = model.GetSymbolInfo(id).Symbol; if (s == null || !dataOnly.ContainsKey(Analyzer.Key(s))) continue;
                                        addEv(Analyzer.Key(s), "寫入儲存鍵「" + key + "」經 " + how + "(第 " + line + " 行)");
                                    }
                                var asg = n.Ancestors().OfType<AssignmentExpressionSyntax>().FirstOrDefault(a => a.Right.Span.Contains(n.Span));
                                if (asg != null) { var ls = model.GetSymbolInfo(asg.Left).Symbol; if (ls != null && dataOnly.ContainsKey(Analyzer.Key(ls))) addEv(Analyzer.Key(ls), "讀自儲存鍵「" + key + "」經 " + how + "(第 " + line + " 行)"); }
                                var vd = n.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault(v => v.Initializer != null && v.Initializer.Span.Contains(n.Span));
                                if (vd != null) { var s = model.GetDeclaredSymbol(vd); if (s != null && dataOnly.ContainsKey(Analyzer.Key(s))) addEv(Analyzer.Key(s), "讀自儲存鍵「" + key + "」(第 " + line + " 行)"); }
                                if (ea != null && n.Parent is AssignmentExpressionSyntax && ((AssignmentExpressionSyntax)n.Parent).Left == n)
                                    foreach (var id in ((AssignmentExpressionSyntax)n.Parent).Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                                    { var s = model.GetSymbolInfo(id).Symbol; if (s != null && dataOnly.ContainsKey(Analyzer.Key(s))) addEv(Analyzer.Key(s), "寫入儲存鍵「" + key + "」經 索引子(第 " + line + " 行)"); }
                            }
                        }
                        var sl = n as LiteralExpressionSyntax;
                        if (sl != null && sl.IsKind(SyntaxKind.StringLiteralExpression) && !literalsByName.ContainsKey(sl.Token.ValueText)) literalsByName[sl.Token.ValueText] = line;

                        // --- UI:事件掛接 ---
                        var ae = n as AssignmentExpressionSyntax;
                        if (ae != null && ae.IsKind(SyntaxKind.AddAssignmentExpression))
                        {
                            var ma = ae.Left as MemberAccessExpressionSyntax; var cs = ma == null ? null : model.GetSymbolInfo(ma.Expression).Symbol;
                            if (cs != null && ctrlKeys.Contains(Analyzer.Key(cs)))
                            {
                                ExpressionSyntax rhs = ae.Right; var oc = rhs as BaseObjectCreationExpressionSyntax;
                                if (oc != null && oc.ArgumentList != null && oc.ArgumentList.Arguments.Count == 1) rhs = oc.ArgumentList.Arguments[0].Expression;
                                var hs = model.GetSymbolInfo(rhs).Symbol as IMethodSymbol;
                                List<object> l; if (!wired.TryGetValue(Analyzer.Key(cs), out l)) wired[Analyzer.Key(cs)] = l = new List<object>();
                                l.Add(new { evt = ma.Name.Identifier.Text, handler = hs == null ? rhs.ToString() : hs.Name, line });
                            }
                        }
                        // --- UI:控制項屬性與資料欄位之間的賦值 ---
                        Action<ExpressionSyntax, ExpressionSyntax> pair = (left, right) =>
                        {
                            var lc = left.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Select(x => new { x, s = model.GetSymbolInfo(x.Expression).Symbol }).FirstOrDefault(z => z.s != null && ctrlKeys.Contains(Analyzer.Key(z.s)));
                            var rc = right.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Select(x => new { x, s = model.GetSymbolInfo(x.Expression).Symbol }).FirstOrDefault(z => z.s != null && ctrlKeys.Contains(Analyzer.Key(z.s)));
                            Func<ExpressionSyntax, ISymbol> dm = e => e.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(i => model.GetSymbolInfo(i).Symbol).FirstOrDefault(s => s != null && dataOnly.ContainsKey(Analyzer.Key(s)));
                            var enc = Analyzer.Enclosing(model, n.SpanStart); var encName = enc == null ? "?" : enc.Name;
                            if (encName == "InitializeComponent") return;
                            if (lc != null && rc == null) { var d = dm(right); if (d != null) AddUi(uiRows, seenPair, lc.s.Name, lc.x.Name.Identifier.Text, d.Name, "資料 → 畫面", encName, line); }
                            else if (rc != null && lc == null) { var d = dm(left); if (d != null) AddUi(uiRows, seenPair, rc.s.Name, rc.x.Name.Identifier.Text, d.Name, "畫面 → 資料", encName, line); }
                        };
                        if (ae != null && ae.IsKind(SyntaxKind.SimpleAssignmentExpression)) pair(ae.Left, ae.Right);
                        var vdd = n as VariableDeclaratorSyntax;
                        if (vdd != null && vdd.Initializer != null) { var ds = model.GetDeclaredSymbol(vdd); if (ds != null && dataOnly.ContainsKey(Analyzer.Key(ds))) { var rcm = vdd.Initializer.Value.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Select(x => new { x, s = model.GetSymbolInfo(x.Expression).Symbol }).FirstOrDefault(z => z.s != null && ctrlKeys.Contains(Analyzer.Key(z.s))); if (rcm != null) AddUi(uiRows, seenPair, rcm.s.Name, rcm.x.Name.Identifier.Text, ds.Name, "畫面 → 資料", "(欄位初始值)", line); } }
                    }
            }
            foreach (var m in dataOnly.Values)
            {
                var k = Analyzer.Key(m);
                if (evid.ContainsKey(k)) continue;
                var nm = Regex.Replace(m.Name, "^(m_|_)", "");
                int ln; if (nm.Length >= 3 && literalsByName.TryGetValue(nm, out ln)) addEv(k, "程式裡有同名字串「" + nm + "」(第 " + ln + " 行),可能是儲存鍵");
            }
            foreach (var m in dataOnly.Values)
            {
                List<string> l; if (!evid.TryGetValue(Analyzer.Key(m), out l)) continue;
                var loc = m.Locations.FirstOrDefault(x => x.IsInSource);
                storage.Add(new { name = m.Name, type = m is IFieldSymbol ? Short(((IFieldSymbol)m).Type) : Short(((IPropertySymbol)m).Type), line = loc == null ? 0 : loc.GetLineSpan().StartLinePosition.Line + 1, evidence = l });
            }

            var controls = ctrls.Select(c =>
            {
                var k = Analyzer.Key(c); var rs = R(k).Where(r => r.EnclosingTypeKey == tkey && r.EnclosingKey != k).ToList();
                var loc = c.Locations.FirstOrDefault(x => x.IsInSource);
                var useIn = rs.Where(r => !r.EnclosingDisplay.EndsWith("InitializeComponent()")).Select(r => r.EnclosingDisplay.Substring(r.EnclosingDisplay.LastIndexOf('.') + 1)).Distinct().ToList();
                return new
                {
                    name = c.Name, type = Short(c is IFieldSymbol ? ((IFieldSymbol)c).Type : ((IPropertySymbol)c).Type), line = loc == null ? 0 : loc.GetLineSpan().StartLinePosition.Line + 1,
                    designer = loc != null && loc.GetLineSpan().Path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase),
                    handlers = wired.ContainsKey(k) ? wired[k] : new List<object>(), usedBy = useIn.Take(10).ToList(), usedByCount = useIn.Count
                };
            }).OrderBy(c => c.name).ToList();

            var tm = Metrics.Types(an, mm.Values.ToList()).FirstOrDefault(x => x.Item.Key == tkey);
            var span = T.DeclaringSyntaxReferences.Select(r => { var ls = r.SyntaxTree.GetLineSpan(r.Span); return ls.EndLinePosition.Line - ls.StartLinePosition.Line + 1; }).Sum();
            return new
            {
                name = item.Display, lines = span, members = T.GetMembers().Count(x => !x.IsImplicitlyDeclared), wmc = tm == null ? 0 : tm.WMC, ca = tm == null ? 0 : tm.Ca, ce = tm == null ? 0 : tm.Ce,
                lcom4, units = units.Count, dataMembers = data.Count, controlCount = ctrls.Count, hub,
                inventory = inventory.OrderByDescending(x => x.Key).Select(x => x.Value).ToList(), clusters, storage, controls, ui = uiRows
            };
        }

        static void AddUi(List<object> rows, HashSet<string> seen, string control, string prop, string member, string dir, string method, int line)
        {
            if (!seen.Add(control + "." + prop + "|" + member + "|" + dir + "|" + method)) return;
            rows.Add(new { control, prop, member, dir, method, line });
        }
    }
}

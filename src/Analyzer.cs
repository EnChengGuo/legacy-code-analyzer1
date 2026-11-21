using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    public class Ref
    {
        public string Project, File, Text, Kind;
        public string TargetKey, EnclosingKey, EnclosingDisplay;
        public string TargetTypeKey, EnclosingTypeKey;
        public int Line;
    }

    public class SymbolInfoItem
    {
        public string Key, Display, Kind, Project, File;
        public int Line;
        public ISymbol Symbol;
    }

    /// <summary>核心語意分析:符號、參照(區分讀寫)、呼叫階層、影響分析、診斷、全文搜尋。</summary>
    public class Analyzer
    {
        static readonly SymbolDisplayFormat Fmt = SymbolDisplayFormat.CSharpErrorMessageFormat;
        readonly List<LoadedProject> projects;
        readonly HashSet<string> assemblyNames;
        Dictionary<string, SymbolInfoItem> declared;
        List<Ref> allRefs;
        Dictionary<string, List<Ref>> byTarget, byEnclosing;

        public Analyzer(List<LoadedProject> projects)
        {
            this.projects = projects;
            assemblyNames = new HashSet<string>(projects.Select(p => p.Name));
        }

        // ---------- 符號 ----------
        public static string Key(ISymbol s)
        {
            s = s.OriginalDefinition;
            var asm = s.ContainingAssembly == null ? "" : s.ContainingAssembly.Name;
            return asm + ":" + (s.GetDocumentationCommentId() ?? s.ToDisplayString(Fmt));
        }

        public Dictionary<string, SymbolInfoItem> Declared()
        {
            if (declared != null) return declared;
            declared = new Dictionary<string, SymbolInfoItem>();
            foreach (var p in projects)
                foreach (var t in p.Compilation.Assembly.GlobalNamespace.GetAllTypes())
                {
                    Add(p, t);
                    foreach (var m in t.GetMembers())
                    {
                        if (m.IsImplicitlyDeclared) continue;
                        if (m is IMethodSymbol ms && (ms.MethodKind == MethodKind.PropertyGet || ms.MethodKind == MethodKind.PropertySet
                            || ms.MethodKind == MethodKind.EventAdd || ms.MethodKind == MethodKind.EventRemove)) continue;
                        if (m is INamedTypeSymbol) continue;
                        Add(p, m);
                    }
                }
            return declared;
        }

        void Add(LoadedProject p, ISymbol s)
        {
            var loc = s.Locations.FirstOrDefault(l => l.IsInSource);
            var span = loc == null ? (FileLinePositionSpan?)null : loc.GetLineSpan();
            declared[Key(s)] = new SymbolInfoItem
            {
                Key = Key(s), Display = s.ToDisplayString(Fmt), Kind = KindName(s), Project = p.Name, Symbol = s,
                File = span == null ? "" : span.Value.Path, Line = span == null ? 0 : span.Value.StartLinePosition.Line + 1
            };
        }

        public static string KindName(ISymbol s)
        {
            var t = s as INamedTypeSymbol;
            if (t != null)
            {
                switch (t.TypeKind)
                {
                    case TypeKind.Interface: return "介面";
                    case TypeKind.Enum: return "列舉";
                    case TypeKind.Struct: return "結構";
                    case TypeKind.Delegate: return "委派";
                    default: return "類別";
                }
            }
            switch (s.Kind)
            {
                case SymbolKind.Method: return ((IMethodSymbol)s).MethodKind == MethodKind.Constructor ? "建構子" : "方法";
                case SymbolKind.Property: return "屬性";
                case SymbolKind.Field: return "欄位";
                case SymbolKind.Event: return "事件";
                default: return s.Kind.ToString();
            }
        }

        /// <summary>依名稱尋找:可寫「Order」、「Order.ApplyDiscount」或完整顯示名稱。</summary>
        public List<SymbolInfoItem> Find(string query)
        {
            var q = query.Trim();
            Func<string, string> bare = s => { var i = s.IndexOf('('); return i < 0 ? s : s.Substring(0, i); };
            return Declared().Values.Where(d =>
                    d.Symbol.Name == q || d.Display == q || bare(d.Display) == q
                    || bare(d.Display).EndsWith("." + q, StringComparison.Ordinal))
                .OrderBy(d => d.Display, StringComparer.Ordinal).ToList();
        }

        // ---------- 參照索引 ----------
        void EnsureIndex()
        {
            if (allRefs != null) return;
            allRefs = new List<Ref>();
            foreach (var p in projects)
                foreach (var tree in p.Compilation.SyntaxTrees)
                {
                    var model = p.Compilation.GetSemanticModel(tree);
                    var lines = tree.GetText();
                    foreach (var node in tree.GetRoot().DescendantNodes())
                    {
                        ISymbol sym = null; SyntaxNode at = node;
                        if (node is SimpleNameSyntax) sym = model.GetSymbolInfo(node).Symbol;
                        else if (node is BaseObjectCreationExpressionSyntax) sym = model.GetSymbolInfo(node).Symbol;
                        else continue;
                        if (sym == null) continue;
                        if (sym is IMethodSymbol m0 && m0.MethodKind == MethodKind.ReducedExtension) sym = m0.ReducedFrom;
                        if (!(sym.Kind == SymbolKind.Method || sym.Kind == SymbolKind.Field || sym.Kind == SymbolKind.Property
                            || sym.Kind == SymbolKind.Event || sym.Kind == SymbolKind.NamedType)) continue;
                        if (sym.ContainingAssembly == null || !assemblyNames.Contains(sym.ContainingAssembly.Name)) continue;

                        var enc = Enclosing(model, node.SpanStart);
                        var line = lines.Lines.GetLineFromPosition(node.SpanStart);
                        allRefs.Add(new Ref
                        {
                            Project = p.Name, File = tree.FilePath, Line = line.LineNumber + 1,
                            Text = line.ToString().Trim(), Kind = Classify(sym, node),
                            TargetKey = Key(sym),
                            TargetTypeKey = TypeKeyOf(sym),
                            EnclosingTypeKey = enc == null ? null : TypeKeyOf(enc),
                            EnclosingKey = enc == null ? "(全域)" : Key(enc),
                            EnclosingDisplay = enc == null ? "(全域)" : enc.ToDisplayString(Fmt)
                        });
                    }
                }
            byTarget = allRefs.GroupBy(r => r.TargetKey).ToDictionary(g => g.Key, g => g.ToList());
            byEnclosing = allRefs.GroupBy(r => r.EnclosingKey).ToDictionary(g => g.Key, g => g.ToList());
        }

        public List<LoadedProject> Projects { get { return projects; } }

        public List<Ref> AllRefs() { EnsureIndex(); return allRefs; }

        static string TypeKeyOf(ISymbol s)
        {
            var t = s as INamedTypeSymbol ?? s.ContainingType;
            return t == null ? null : Key(t);
        }

        internal static ISymbol Enclosing(SemanticModel model, int pos)
        {
            var s = model.GetEnclosingSymbol(pos);
            while (s != null)
            {
                var m = s as IMethodSymbol;
                if (m != null && (m.MethodKind == MethodKind.AnonymousFunction || m.MethodKind == MethodKind.LocalFunction)) { s = s.ContainingSymbol; continue; }
                if (m != null && m.AssociatedSymbol != null) return m.AssociatedSymbol;
                return s;
            }
            return null;
        }

        static string Classify(ISymbol sym, SyntaxNode node)
        {
            if (node is BaseObjectCreationExpressionSyntax) return "建立物件";
            if (sym.Kind == SymbolKind.NamedType) return "型別使用";
            SyntaxNode expr = node;
            var ma = node.Parent as MemberAccessExpressionSyntax;
            if (ma != null && ma.Name == node) expr = ma;
            var cb = expr.Parent as ConditionalAccessExpressionSyntax; // a?.B 的 B 在 MemberBinding 內
            var mb = node.Parent as MemberBindingExpressionSyntax;
            if (mb != null) expr = mb;

            if (sym.Kind == SymbolKind.Method)
                return expr.Parent is InvocationExpressionSyntax inv && inv.Expression == expr ? "呼叫" : "方法群組參照";

            var parent = expr.Parent;
            var asg = parent as AssignmentExpressionSyntax;
            if (asg != null && asg.Left == expr)
                return asg.IsKind(SyntaxKind.SimpleAssignmentExpression) ? "寫" : "讀寫";
            var pre = parent as PrefixUnaryExpressionSyntax;
            if (pre != null && (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression))) return "讀寫";
            if (parent is PostfixUnaryExpressionSyntax && (parent.IsKind(SyntaxKind.PostIncrementExpression) || parent.IsKind(SyntaxKind.PostDecrementExpression))) return "讀寫";
            var arg = parent as ArgumentSyntax;
            if (arg != null)
            {
                if (arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) return "寫";
                if (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)) return "讀寫";
            }
            // 物件初始設定式 new X { Name = 1 } 的 Name 是寫入
            var ini = parent as AssignmentExpressionSyntax;
            if (ini != null && ini.Left == node) return "寫";
            return "讀";
        }

        public List<Ref> References(SymbolInfoItem target)
        {
            EnsureIndex();
            List<Ref> list;
            return byTarget.TryGetValue(target.Key, out list)
                ? list.OrderBy(r => r.File).ThenBy(r => r.Line).ToList() : new List<Ref>();
        }

        // ---------- 呼叫階層 ----------
        /// <summary>目標被呼叫時可能經過的鍵:本身、覆寫的基底成員、所實作的介面成員。</summary>
        HashSet<string> EquivalentKeys(ISymbol s)
        {
            var keys = new HashSet<string> { Key(s) };
            var m = s as IMethodSymbol; var p = s as IPropertySymbol;
            ISymbol cur = s;
            while (true)
            {
                ISymbol next = cur is IMethodSymbol cm ? cm.OverriddenMethod : cur is IPropertySymbol cp ? cp.OverriddenProperty : null;
                if (next == null) break;
                keys.Add(Key(next)); cur = next;
            }
            if ((m != null || p != null) && s.ContainingType != null)
                foreach (var itf in s.ContainingType.AllInterfaces)
                    foreach (var im in itf.GetMembers())
                    {
                        var impl = s.ContainingType.FindImplementationForInterfaceMember(im);
                        if (impl != null && SymbolEqualityComparer.Default.Equals(impl.OriginalDefinition, s.OriginalDefinition)) keys.Add(Key(im));
                    }
            return keys;
        }

        public List<Ref> IncomingCalls(SymbolInfoItem target)
        {
            EnsureIndex();
            var res = new List<Ref>();
            foreach (var k in EquivalentKeys(target.Symbol))
            {
                List<Ref> l;
                if (byTarget.TryGetValue(k, out l)) res.AddRange(l.Where(r => r.Kind != "型別使用" && r.EnclosingKey != target.Key));
            }
            return res;
        }

        public List<Ref> OutgoingCalls(SymbolInfoItem source)
        {
            EnsureIndex();
            List<Ref> l;
            if (!byEnclosing.TryGetValue(source.Key, out l)) return new List<Ref>();
            return l.Where(r => r.Kind == "呼叫" || r.Kind == "建立物件" || r.Kind == "方法群組參照").ToList();
        }

        /// <summary>影響分析:找出所有(直接與間接)會用到目標的成員,依層數分組。</summary>
        public List<KeyValuePair<int, SymbolInfoItem>> Impact(SymbolInfoItem target, int maxDepth)
        {
            EnsureIndex();
            var seen = new HashSet<string> { target.Key };
            var result = new List<KeyValuePair<int, SymbolInfoItem>>();
            var frontier = new List<SymbolInfoItem> { target };
            for (int depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
            {
                var next = new List<SymbolInfoItem>();
                foreach (var f in frontier)
                    foreach (var r in IncomingCalls(f))
                    {
                        if (!seen.Add(r.EnclosingKey)) continue;
                        SymbolInfoItem item;
                        if (!Declared().TryGetValue(r.EnclosingKey, out item)) continue;
                        result.Add(new KeyValuePair<int, SymbolInfoItem>(depth, item));
                        next.Add(item);
                    }
                frontier = next;
            }
            return result;
        }

        // ---------- 診斷與搜尋 ----------
        public List<string> Diagnostics()
        {
            var res = new List<string>();
            foreach (var p in projects)
                foreach (var d in p.Compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning))
                {
                    var sp = d.Location.GetLineSpan();
                    res.Add((d.Severity == DiagnosticSeverity.Error ? "錯誤" : "警告") + " " + d.Id + " "
                        + (sp.Path == "" ? p.Name : Path.GetFileName(sp.Path) + ":" + (sp.StartLinePosition.Line + 1))
                        + " " + d.GetMessage());
                }
            return res;
        }

        public List<string> Search(string text)
        {
            var res = new List<string>();
            foreach (var p in projects)
                foreach (var f in p.SourceFiles)
                {
                    int n = 0;
                    foreach (var line in File.ReadLines(f))
                    {
                        n++;
                        if (line.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                            res.Add(Path.GetFileName(f) + ":" + n + "  " + line.Trim());
                    }
                }
            return res;
        }
    }
}

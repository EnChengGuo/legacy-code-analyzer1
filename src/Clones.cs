using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    public class CloneSite { public string Member, File; public int StartLine, EndLine; }
    public class Clone { public int Tokens, Lines; public bool Exact; public CloneSite A, B; }

    /// <summary>
    /// 重複程式碼偵測(以語法標記比對):把每個方法內文轉成標記序列,
    /// 識別字與字面值先一律視為同一種(所以改過變數名稱、常數的重複也抓得到),
    /// 用固定長度的視窗比對,連續命中的視窗合併成一段重複。
    /// </summary>
    public static class Clones
    {
        class Unit
        {
            public string Display, File; public int[] Ids; public string[] Raw; public int[] LineS, LineE;
        }

        static IEnumerable<KeyValuePair<ISymbol, SyntaxNode>> Bodies(Analyzer an, Func<string, bool> fileOk)
        {
            foreach (var p in an.Projects)
                foreach (var tree in p.Compilation.SyntaxTrees)
                {
                    if (!fileOk(tree.FilePath)) continue;
                    var model = p.Compilation.GetSemanticModel(tree);
                    foreach (var node in tree.GetRoot().DescendantNodes())
                    {
                        SyntaxNode body = null; ISymbol sym = null;
                        var md = node as BaseMethodDeclarationSyntax;
                        if (md != null) { body = (SyntaxNode)md.Body ?? md.ExpressionBody; if (body != null) sym = model.GetDeclaredSymbol(md); }
                        else if (node is AccessorDeclarationSyntax)
                        {
                            var ad = (AccessorDeclarationSyntax)node; body = (SyntaxNode)ad.Body ?? ad.ExpressionBody;
                            if (body != null) { var ms = model.GetDeclaredSymbol(ad) as IMethodSymbol; sym = ms == null ? null : (ms.AssociatedSymbol ?? ms); }
                        }
                        if (body != null && sym != null) yield return new KeyValuePair<ISymbol, SyntaxNode>(sym, body);
                    }
                }
        }

        public static List<Clone> Find(Analyzer an, int window, int minLines, bool exactOnly, bool includeGenerated)
        {
            Func<string, bool> fileOk = f => includeGenerated || !(f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase));
            var dict = new Dictionary<string, int>();
            Func<string, int> id = s => { int v; if (!dict.TryGetValue(s, out v)) dict[s] = v = dict.Count + 1; return v; };
            var units = new List<Unit>();
            foreach (var kv in Bodies(an, fileOk))
            {
                var toks = kv.Value.DescendantTokens().ToList();
                if (toks.Count < window) continue;
                var u = new Unit { Display = kv.Key.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), File = kv.Value.SyntaxTree.FilePath };
                u.Ids = new int[toks.Count]; u.Raw = new string[toks.Count]; u.LineS = new int[toks.Count]; u.LineE = new int[toks.Count];
                var text = kv.Value.SyntaxTree.GetText();
                for (int i = 0; i < toks.Count; i++)
                {
                    var t = toks[i]; var k = t.Kind();
                    u.Raw[i] = t.Text;
                    string norm = k == SyntaxKind.IdentifierToken ? (exactOnly ? "I:" + t.Text : "I")
                        : (k == SyntaxKind.NumericLiteralToken || k == SyntaxKind.StringLiteralToken || k == SyntaxKind.CharacterLiteralToken
                            || k == SyntaxKind.InterpolatedStringTextToken) ? (exactOnly ? "L:" + t.Text : "L")
                        : ((int)k).ToString();
                    u.Ids[i] = id(norm);
                    var ls = text.Lines.GetLinePosition(t.Span.Start).Line; var le = text.Lines.GetLinePosition(t.Span.End).Line;
                    u.LineS[i] = ls + 1; u.LineE[i] = le + 1;
                }
                units.Add(u);
            }

            // 視窗雜湊
            const ulong B = 1000003UL;
            var buckets = new Dictionary<ulong, List<int[]>>();
            for (int ui = 0; ui < units.Count; ui++)
            {
                var ids = units[ui].Ids; int n = ids.Length;
                ulong h = 0, pow = 1;
                for (int i = 0; i < window; i++) { h = h * B + (ulong)ids[i]; if (i < window - 1) pow *= B; }
                for (int i = 0; ; i++)
                {
                    List<int[]> l; if (!buckets.TryGetValue(h, out l)) buckets[h] = l = new List<int[]>(2);
                    l.Add(new[] { ui, i });
                    if (i + window >= n) break;
                    h = (h - (ulong)ids[i] * pow) * B + (ulong)ids[i + window];
                }
            }

            // 同一對方法、同一個位移量上的連續命中併成一段
            var diag = new Dictionary<string, List<int>>();
            foreach (var b in buckets.Values)
            {
                if (b.Count < 2 || b.Count > 40) continue; // 太常見的樣板(例如一長串屬性指派)不當作重複
                for (int x = 0; x < b.Count; x++)
                    for (int y = x + 1; y < b.Count; y++)
                    {
                        var p = b[x]; var q = b[y];
                        if (p[0] > q[0] || (p[0] == q[0] && p[1] > q[1])) { var t = p; p = q; q = t; }
                        if (p[0] == q[0] && q[1] - p[1] < window) continue; // 同一方法內重疊的視窗
                        var ia = units[p[0]].Ids; var ib = units[q[0]].Ids; bool same = true;
                        for (int k = 0; k < window; k++) if (ia[p[1] + k] != ib[q[1] + k]) { same = false; break; }
                        if (!same) continue; // 雜湊碰撞
                        var key = p[0] + "," + q[0] + "," + (q[1] - p[1]);
                        List<int> l; if (!diag.TryGetValue(key, out l)) diag[key] = l = new List<int>();
                        l.Add(p[1]);
                    }
            }

            var res = new List<Clone>();
            foreach (var kv in diag)
            {
                var parts = kv.Key.Split(','); int ua = int.Parse(parts[0]), ub = int.Parse(parts[1]), off = int.Parse(parts[2]);
                var pos = kv.Value.Distinct().OrderBy(x => x).ToList();
                int s = pos[0], e = pos[0] + window;
                Action flush = () =>
                {
                    var A = units[ua]; var Bu = units[ub];
                    int la = A.LineE[e - 1] - A.LineS[s] + 1, lb = Bu.LineE[e - 1 + off] - Bu.LineS[s + off] + 1;
                    if (Math.Max(la, lb) < minLines) return;
                    bool exact = true;
                    for (int k = s; k < e && exact; k++) if (A.Raw[k] != Bu.Raw[k + off]) exact = false;
                    res.Add(new Clone
                    {
                        Tokens = e - s, Lines = Math.Max(la, lb), Exact = exact,
                        A = new CloneSite { Member = A.Display, File = A.File, StartLine = A.LineS[s], EndLine = A.LineE[e - 1] },
                        B = new CloneSite { Member = Bu.Display, File = Bu.File, StartLine = Bu.LineS[s + off], EndLine = Bu.LineE[e - 1 + off] }
                    });
                };
                for (int i = 1; i < pos.Count; i++)
                {
                    if (pos[i] <= e) e = pos[i] + window; else { flush(); s = pos[i]; e = pos[i] + window; }
                }
                flush();
            }
            // 依長度排序,最長的先看(尚未把「被更長的重複涵蓋」的短片段去除)
            return res.OrderByDescending(c => c.Tokens).ThenBy(c => c.A.File).ToList();
        }
    }
}

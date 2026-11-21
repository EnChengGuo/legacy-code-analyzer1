using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeAtlas
{
    public class Commit { public string Hash, Author, Date, Subject; }
    public class Editors
    {
        public Commit Last; public List<KeyValuePair<string, string>> Previous = new List<KeyValuePair<string, string>>(); // 作者, 最後改的日期
        public int Commits; public string Note;
    }

    /// <summary>
    /// 冤親債主:查某段程式碼是誰最後改的、再往前是哪幾代的人改的。
    /// 呼叫系統上的 git(依序找 PATH、Git for Windows 安裝位置、Visual Studio 內附的 git),不需要安裝任何東西。
    /// 用 git log -L 追蹤「那幾行」的歷史,所以不會被同檔案裡其他人改別的地方干擾。
    /// </summary>
    public class GitInfo
    {
        readonly string git; readonly Dictionary<string, string> rootCache = new Dictionary<string, string>();
        readonly Dictionary<string, Editors> cache = new Dictionary<string, Editors>();
        public string Problem { get; private set; }

        public GitInfo()
        {
            git = FindGit();
            if (git == null) Problem = "找不到 git。請確認 git 在 PATH,或安裝 Git for Windows(Visual Studio 通常內附)。";
        }

        static string FindGit()
        {
            var cands = new List<string> { "git" };
            foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("LOCALAPPDATA") })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                cands.Add(Path.Combine(pf, "Git", "cmd", "git.exe")); cands.Add(Path.Combine(pf, "Programs", "Git", "cmd", "git.exe"));
                var vs = Path.Combine(pf, "Microsoft Visual Studio");
                if (Directory.Exists(vs))
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(vs, "git.exe", SearchOption.AllDirectories).Take(1)) cands.Add(f);
                    }
                    catch { }
            }
            foreach (var c in cands)
            {
                try { var r = Run(c, null, "--version", 5000); if (r != null && r.StartsWith("git version")) return c; } catch { }
            }
            return null;
        }

        static string Run(string exe, string dir, string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            if (dir != null) psi.WorkingDirectory = dir;
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                var o = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                return p.ExitCode == 0 ? o.Result : null;
            }
        }

        string Root(string file)
        {
            var dir = Path.GetDirectoryName(file);
            string r; if (rootCache.TryGetValue(dir, out r)) return r;
            var o = git == null ? null : Run(git, dir, "rev-parse --show-toplevel", 10000);
            r = o == null ? null : o.Trim().Replace('/', Path.DirectorySeparatorChar);
            rootCache[dir] = r; return r;
        }

        public Editors History(string file, int start, int end, int max)
        {
            var ed = new Editors();
            if (git == null) { ed.Note = Problem; return ed; }
            var key = file + ":" + start + "-" + end;
            Editors c; if (cache.TryGetValue(key, out c)) return c;
            var root = Root(file);
            if (root == null) { ed.Note = "這個檔案不在 git 儲存庫內。"; return cache[key] = ed; }
            var rel = file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            var o = Run(git, root, "log -L " + start + "," + end + ":\"" + rel + "\" -s -n " + max + " --date=short --format=%H%x09%an%x09%ad%x09%s", 20000);
            if (o == null) { ed.Note = "查不到歷史(檔案可能還沒提交,或 git 版本不支援)。"; return cache[key] = ed; }
            var commits = o.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t')).Where(p => p.Length >= 4)
                .Select(p => new Commit { Hash = p[0].Substring(0, Math.Min(8, p[0].Length)), Author = p[1], Date = p[2], Subject = p[3] }).ToList();
            ed.Commits = commits.Count;
            if (commits.Count == 0) { ed.Note = "沒有提交紀錄(可能還沒提交)。"; return cache[key] = ed; }
            ed.Last = commits[0];
            var seen = new HashSet<string> { ed.Last.Author };
            foreach (var cm in commits.Skip(1)) if (seen.Add(cm.Author)) ed.Previous.Add(new KeyValuePair<string, string>(cm.Author, cm.Date));
            return cache[key] = ed;
        }

        // ---------- 沒有註解的成員 ----------
        static bool HasComment(SyntaxNode decl)
        {
            Func<SyntaxTriviaList, bool> any = t => t.Any(x => x.IsKind(SyntaxKind.SingleLineCommentTrivia) || x.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || x.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || x.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
            return any(decl.GetLeadingTrivia()) || any(decl.GetLastToken().TrailingTrivia);
        }

        public class Debtor { public SymbolInfoItem Item; public int Start, End; public Editors Editors; }

        /// <summary>類別裡「沒寫註解」的成員,以及各自的最後編輯者與歷代編輯者。</summary>
        public List<Debtor> Debtors(Analyzer an, SymbolInfoItem type, int limit, int maxCommits)
        {
            var res = new List<Debtor>();
            var t = type.Symbol as INamedTypeSymbol; if (t == null) return res;
            foreach (var m in t.GetMembers().Where(x => !x.IsImplicitlyDeclared && !(x is INamedTypeSymbol)
                && !(x is IMethodSymbol && ((IMethodSymbol)x).AssociatedSymbol != null)))
            {
                var r = m.DeclaringSyntaxReferences.FirstOrDefault(); if (r == null) continue;
                var node = r.GetSyntax();
                if (node is VariableDeclaratorSyntax) node = node.Parent != null && node.Parent.Parent != null ? node.Parent.Parent : node; // 欄位:整個欄位宣告
                if (HasComment(node)) continue;
                var ls = r.SyntaxTree.GetLineSpan(node.Span);
                SymbolInfoItem item; if (!an.Declared().TryGetValue(Analyzer.Key(m), out item)) continue;
                res.Add(new Debtor { Item = item, Start = ls.StartLinePosition.Line + 1, End = ls.EndLinePosition.Line + 1 });
            }
            res = res.OrderBy(d => d.Start).ToList();
            foreach (var d in res.Take(limit)) d.Editors = History(d.Item.File, d.Start, d.End, maxCommits);
            return res;
        }

        /// <summary>一個符號(類別或成員)的最後編輯者,給右側資訊欄用。</summary>
        public Editors ForSymbol(SymbolInfoItem it, int maxCommits)
        {
            var r = it.Symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (r == null) return new Editors { Note = "沒有原始碼位置。" };
            var ls = r.SyntaxTree.GetLineSpan(r.Span);
            int a = ls.StartLinePosition.Line + 1, b = ls.EndLinePosition.Line + 1;
            if (it.Symbol is INamedTypeSymbol && b - a > 2000) // 巨大的類別:只追宣告那幾行會失真,改看整個檔案最近誰動過
            { var e = History(it.File, 1, Math.Min(b, 1), maxCommits); e.Note = "類別太大,只顯示檔案開頭(宣告處)的歷史。成員請看各自的編輯者。"; return e; }
            return History(it.File, a, b, maxCommits);
        }
    }
}

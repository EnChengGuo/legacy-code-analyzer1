using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace CodeAtlas
{
    /// <summary>「開啟專案」:從路徑(或網頁上的資料夾瀏覽)載入 .sln / .csproj,載入在背景進行,網頁輪詢進度。</summary>
    public partial class Server
    {
        readonly object stLock = new object();
        string state = "idle", openError, curPath, curName, curSources, openingPath;
        readonly List<string> openLog = new List<string>();
        DateTime curAt; int curProjects, curFiles, curTypes;

        static string AppDataDir()
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeAtlas");
            Directory.CreateDirectory(d); return d;
        }

        void LogLine(string s) { lock (stLock) { if (openLog.Count < 500) openLog.Add(s); } }

        /// <summary>命令列已經載好專案時,把狀態補上。</summary>
        void SetLoaded(string path, string sourcesText)
        {
            lock (stLock)
            {
                state = "ready"; openError = null; curPath = path; curSources = sourcesText ?? ""; curAt = DateTime.Now;
                curName = Path.GetFileNameWithoutExtension(path);
                curProjects = an.Projects.Count; curFiles = an.Projects.Sum(p => p.SourceFiles.Count);
                curTypes = an.Declared().Values.Count(d => d.Symbol is Microsoft.CodeAnalysis.INamedTypeSymbol);
            }
        }

        object StatusJson()
        {
            lock (stLock)
            {
                return new
                {
                    state, loaded = an != null, opening = openingPath, path = curPath, name = curName, sources = curSources, at = curAt == default(DateTime) ? "" : curAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    projects = curProjects, files = curFiles, types = curTypes, error = openError, log = openLog.Skip(Math.Max(0, openLog.Count - 60)).ToList()
                };
            }
        }

        // ---------- 把使用者輸入的路徑解析成一個 .sln 或 .csproj ----------
        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", "packages", "TestResults" };

        static IEnumerable<string> FindProjectFiles(string dir, int depth)
        {
            string[] slns = new string[0], projs = new string[0];
            try { slns = Directory.GetFiles(dir, "*.sln"); projs = Directory.GetFiles(dir, "*.csproj"); } catch { }
            foreach (var s in slns) yield return s;
            if (slns.Length == 0) foreach (var p in projs) yield return p; // 有 .sln 就不另外列同資料夾的 .csproj
            if (depth <= 0) yield break;
            string[] subs = new string[0];
            try { subs = Directory.GetDirectories(dir); } catch { }
            foreach (var sd in subs)
            {
                if (SkipDirs.Contains(Path.GetFileName(sd))) continue;
                foreach (var f in FindProjectFiles(sd, depth - 1)) yield return f;
            }
        }

        /// <summary>回傳 null 並設定 error/candidates,或回傳單一檔案路徑。</summary>
        static string Resolve(string raw, out string error, out List<string> candidates)
        {
            error = null; candidates = null;
            var p = Environment.ExpandEnvironmentVariables((raw ?? "").Trim().Trim('"').Trim());
            if (p == "") { error = "請輸入路徑。"; return null; }
            try { p = Path.GetFullPath(p); } catch { error = "路徑格式不對:" + p; return null; }
            if (File.Exists(p))
            {
                if (p.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return p;
                error = "只支援 .sln 或 .csproj 檔案(你輸入的是 " + Path.GetExtension(p) + ")。"; return null;
            }
            if (Directory.Exists(p))
            {
                var found = FindProjectFiles(p, 3).Take(60).ToList();
                if (found.Count == 0) { error = "這個資料夾(含下三層)裡找不到 .sln 或 .csproj。"; return null; }
                if (found.Count == 1) return found[0];
                candidates = found; error = "這個資料夾裡有多個專案或方案,請選一個。"; return null;
            }
            error = "找不到這個路徑:" + p; return null;
        }

        static Dictionary<string, string> ParseSources(string text, string slnPath)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var baseDir = Path.GetDirectoryName(slnPath) ?? ".";
            Action<string, string> add = (line, rel) =>
            {
                var t = line.Trim(); if (t == "" || t.StartsWith("#")) return;
                var eq = t.IndexOf('='); if (eq <= 0) return;
                var key = t.Substring(0, eq).Trim(); var val = t.Substring(eq + 1).Trim().Trim('"');
                if (key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) key = key.Substring(0, key.Length - 4);
                if (!dict.ContainsKey(key)) dict[key] = Path.GetFullPath(Path.IsPathRooted(val) ? val : Path.Combine(rel, val));
            };
            foreach (var l in (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) add(l, baseDir);
            var f = Path.Combine(baseDir, "codeatlas.sources");
            if (File.Exists(f)) foreach (var l in File.ReadAllLines(f, Encoding.UTF8)) add(l, baseDir);
            return dict;
        }

        object OpenApi(HttpListenerContext ctx)
        {
            string body; using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
            var req = json.Deserialize<Dictionary<string, object>>(body) ?? new Dictionary<string, object>();
            string raw = req.ContainsKey("path") ? Convert.ToString(req["path"]) : "", src = req.ContainsKey("sources") ? Convert.ToString(req["sources"]) : "";
            string err; List<string> cands;
            var file = Resolve(raw, out err, out cands);
            if (file == null) return new { error = err, candidates = cands };
            lock (stLock)
            {
                if (state == "loading") return new { error = "已有專案正在載入,請等它完成。" };
                state = "loading"; openError = null; openingPath = file; openLog.Clear();
            }
            var t = new Thread(() => LoadProject(file, src)) { IsBackground = true };
            t.Start();
            return new { error = (string)null, started = true, path = file };
        }

        void LoadProject(string file, string sourcesText)
        {
            try
            {
                LogLine("讀取 " + file);
                var sources = ParseSources(sourcesText, file);
                foreach (var kv in sources) LogLine("以原始碼取代 DLL「" + kv.Key + "」→ " + kv.Value);
                var projects = ProjectLoader.Load(file, m => LogLine(m), sources);
                LogLine("建立語意模型(" + projects.Count + " 個專案," + projects.Sum(p => p.SourceFiles.Count) + " 個原始檔)…");
                var newAn = new Analyzer(projects);
                newAn.Declared();
                LogLine("分析類別之間的關係…");
                var newDiagram = new DiagramBuilder(newAn);
                lock (gate)
                {
                    an = newAn; diagram = newDiagram; dashboardHtml = null; git = null; memberMetrics = null; typeMetrics = null;
                }
                SetLoaded(file, sourcesText);
                SaveRecent(file, sourcesText);
                LogLine("完成。");
            }
            catch (Exception ex)
            {
                lock (stLock) { state = an != null ? "ready" : "error"; openError = ex.Message; }
                LogLine("失敗:" + ex.Message);
            }
            finally { lock (stLock) { if (state == "loading") state = an != null ? "ready" : "error"; openingPath = null; } }
        }

        // ---------- 資料夾瀏覽(瀏覽器拿不到完整路徑,所以由本機程式列出) ----------
        object BrowseJson(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    var drives = DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch { return false; } }).Select(d => d.Name).ToList();
                    var shortcuts = new List<object>();
                    Action<string, string> sc = (n, p) => { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) shortcuts.Add(new { name = n, path = p }); };
                    sc("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                    sc("文件", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    sc("使用者資料夾", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    return new { error = (string)null, path = "", parent = (string)null, dirs = drives, files = new List<string>(), shortcuts };
                }
                var full = Path.GetFullPath(path.Trim().Trim('"'));
                if (File.Exists(full)) full = Path.GetDirectoryName(full);
                if (!Directory.Exists(full)) return new { error = "找不到資料夾:" + full };
                var dirs = new List<string>(); var files = new List<string>();
                foreach (var d in Directory.GetDirectories(full).Take(2000))
                {
                    var n = Path.GetFileName(d);
                    try { if ((new DirectoryInfo(d).Attributes & FileAttributes.Hidden) != 0) continue; } catch { }
                    dirs.Add(n);
                }
                foreach (var f in Directory.GetFiles(full))
                    if (f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) files.Add(Path.GetFileName(f));
                dirs.Sort(StringComparer.OrdinalIgnoreCase); files.Sort(StringComparer.OrdinalIgnoreCase);
                var parent = Directory.GetParent(full);
                return new { error = (string)null, path = full, parent = parent == null ? "" : parent.FullName, dirs, files, shortcuts = new List<object>() };
            }
            catch (UnauthorizedAccessException) { return new { error = "沒有權限讀取這個資料夾。" }; }
            catch (Exception ex) { return new { error = ex.Message }; }
        }

        // ---------- 最近開啟 ----------
        class RecentItem { public string path { get; set; } public string sources { get; set; } public string when { get; set; } }

        List<RecentItem> LoadRecent()
        {
            try
            {
                var f = Path.Combine(AppDataDir(), "recent.json");
                if (File.Exists(f)) return json.Deserialize<List<RecentItem>>(File.ReadAllText(f, Encoding.UTF8)) ?? new List<RecentItem>();
            }
            catch { }
            return new List<RecentItem>();
        }

        void StoreRecent(List<RecentItem> l)
        {
            try { File.WriteAllText(Path.Combine(AppDataDir(), "recent.json"), json.Serialize(l), new UTF8Encoding(false)); } catch { }
        }

        void SaveRecent(string path, string sources)
        {
            lock (stLock)
            {
                var l = LoadRecent(); l.RemoveAll(x => string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase));
                l.Insert(0, new RecentItem { path = path, sources = sources ?? "", when = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
                StoreRecent(l.Take(12).ToList());
            }
        }

        object RecentApi(HttpListenerContext ctx, System.Collections.Specialized.NameValueCollection q)
        {
            lock (stLock)
            {
                var l = LoadRecent();
                if (ctx.Request.HttpMethod == "POST" && !string.IsNullOrEmpty(q["forget"]))
                {
                    l.RemoveAll(x => string.Equals(x.path, q["forget"], StringComparison.OrdinalIgnoreCase)); StoreRecent(l);
                }
                return l.Select(x => new { x.path, x.sources, x.when, exists = File.Exists(x.path) }).ToList();
            }
        }
    }
}

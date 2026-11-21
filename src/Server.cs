using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.CodeAnalysis;

namespace CodeAtlas
{
    /// <summary>本機網頁伺服器:只聽 localhost,不需要系統管理員權限,不需安裝任何東西。</summary>
    public partial class Server
    {
        Analyzer an; DiagramBuilder diagram; // 還沒開啟專案時是 null;開啟或換專案時整組替換(替換時持有 gate)
        readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 };
        readonly object gate = new object();
        string dashboardHtml;

        /// <param name="an">已載入的專案;null 表示先啟動、再由網頁選專案。</param>
        public Server(Analyzer an, string path = null, string sourcesText = null)
        {
            this.an = an;
            if (an != null) { diagram = new DiagramBuilder(an); if (path != null) SetLoaded(path, sourcesText); }
        }

        static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".js", "application/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" }
        };

        public void Run(int port, bool open)
        {
            HttpListener l = null; int p = port;
            for (int i = 0; i < 10 && l == null; i++, p++) // 預設埠號被占用時,自動往後找一個
            {
                try { var t = new HttpListener(); t.Prefixes.Add("http://localhost:" + p + "/"); t.Start(); l = t; }
                catch (HttpListenerException) { }
            }
            if (l == null) throw new Exception("埠號 " + port + " 到 " + (port + 9) + " 都被占用,請關掉其他的 CodeAtlas 或指定別的埠號(serve 埠號)。");
            var url = "http://localhost:" + (p - 1) + "/";
            if (p - 1 != port) Console.WriteLine("埠號 " + port + " 被占用,改用 " + (p - 1) + "。");
            Console.WriteLine("CodeAtlas 已啟動:" + url + "  (按 Ctrl+C 或關閉這個視窗結束)");
            if (an == null) Console.WriteLine("請在瀏覽器裡選擇要開啟的專案。");
            var start = an == null ? url + "open" : url;
            if (open) try { Process.Start(start); } catch { Console.WriteLine("無法自動開啟瀏覽器,請手動開啟:" + start); }
            while (true)
            {
                var ctx = l.GetContext();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(ctx); }
                    catch (Exception ex)
                    {
                        try { Send(ctx, 500, "application/json; charset=utf-8", json.Serialize(new { error = ex.Message })); } catch { }
                    }
                });
            }
        }

        /// <summary>
        /// 自己解析查詢字串並固定用 UTF-8:HttpListener 內建的 QueryString 會依請求的內容編碼(沒有就用系統預設,
        /// 繁體中文 Windows 是 Big5)解碼,GET 請求的中文名稱會變亂碼。
        /// </summary>
        static System.Collections.Specialized.NameValueCollection Query(HttpListenerContext ctx)
        {
            var nv = new System.Collections.Specialized.NameValueCollection();
            var qs = ctx.Request.Url.Query ?? "";
            if (qs.StartsWith("?")) qs = qs.Substring(1);
            foreach (var part in qs.Split('&'))
            {
                if (part == "") continue;
                var i = part.IndexOf('=');
                var k = i < 0 ? part : part.Substring(0, i); var v = i < 0 ? "" : part.Substring(i + 1);
                nv[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' '));
            }
            return nv;
        }

        // ---------- 版面儲存:存在使用者的 AppData\CodeAtlas\layouts,換埠號或瀏覽器都還在 ----------
        static string Safe(string s)
        {
            var t = new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 127).ToArray());
            return t.Length > 60 ? t.Substring(0, 60) : t;
        }

        void LayoutApi(HttpListenerContext ctx)
        {
            var q = Query(ctx); var page = Safe(q["page"]); var name = Safe(q["name"]);
            if (page == "") { Send(ctx, 400, Mime[".json"], "{\"error\":\"缺少頁面名稱\"}"); return; }
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeAtlas", "layouts");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, page + "__" + name + ".json");
            var m = ctx.Request.HttpMethod;
            if (m == "GET" && name == "")
            {
                var names = Directory.GetFiles(dir, page + "__*.json").Select(f => Path.GetFileNameWithoutExtension(f).Substring(page.Length + 2))
                    .OrderBy(x => x).ToList();
                Send(ctx, 200, Mime[".json"], json.Serialize(names)); return;
            }
            if (name == "") { Send(ctx, 400, Mime[".json"], "{\"error\":\"請輸入版面名稱\"}"); return; }
            if (m == "GET")
            {
                if (!File.Exists(file)) { Send(ctx, 404, Mime[".json"], "{\"error\":\"找不到這個版面\"}"); return; }
                Send(ctx, 200, Mime[".json"], File.ReadAllText(file, Encoding.UTF8)); return;
            }
            if (m == "POST")
            {
                string body; using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
                if (body.Length > 16 * 1024 * 1024) { Send(ctx, 413, Mime[".json"], "{\"error\":\"版面太大\"}"); return; }
                File.WriteAllText(file, body, new UTF8Encoding(false));
                Send(ctx, 200, Mime[".json"], "{\"ok\":true}"); return;
            }
            if (m == "DELETE") { if (File.Exists(file)) File.Delete(file); Send(ctx, 200, Mime[".json"], "{\"ok\":true}"); return; }
            Send(ctx, 405, Mime[".json"], "{\"error\":\"不支援的方法\"}");
        }

        // ---------- 第七階段:符號資訊、冤親債主、重複程式碼、God Class ----------
        GitInfo git; List<MemberMetric> memberMetrics; List<TypeMetric> typeMetrics;
        GitInfo Git() { return git ?? (git = new GitInfo()); }
        void EnsureMetrics() { if (memberMetrics == null) { memberMetrics = Metrics.Members(an); typeMetrics = Metrics.Types(an, memberMetrics); } }
        static int ToInt(string s, int d, int lo, int hi) { int v; return int.TryParse(s, out v) ? Math.Max(lo, Math.Min(hi, v)) : d; }

        static object EdJson(Editors e)
        {
            return new
            {
                last = e.Last == null ? null : new { author = e.Last.Author, date = e.Last.Date, subject = e.Last.Subject, hash = e.Last.Hash },
                previous = e.Previous.Select(p => new { author = p.Key, date = p.Value }).ToList(), commits = e.Commits, note = e.Note
            };
        }

        SymbolInfoItem Pick(System.Collections.Specialized.NameValueCollection q, bool typesOnly, out string error)
        {
            error = null; SymbolInfoItem it;
            if (!string.IsNullOrEmpty(q["key"]) && an.Declared().TryGetValue(q["key"], out it)) return it;
            var name = (q["name"] ?? "").Trim();
            if (name == "") { error = "請指定名稱。"; return null; }
            var c = an.Find(name).Where(d => !typesOnly || d.Symbol is INamedTypeSymbol).ToList();
            var exact = c.Where(d => d.Display == name).ToList(); if (exact.Count == 1) c = exact;
            if (c.Count == 0) { error = "找不到:" + name; return null; }
            if (c.Count > 1) { error = "名稱不唯一,請寫完整一點:" + string.Join("、", c.Take(5).Select(x => x.Display)); return null; }
            return c[0];
        }

        object SymbolJson(System.Collections.Specialized.NameValueCollection q)
        {
            string err; var it = Pick(q, false, out err); if (it == null) return new { error = err };
            EnsureMetrics();
            var r = it.Symbol.DeclaringSyntaxReferences.FirstOrDefault(); int endLine = it.Line;
            if (r != null) endLine = r.SyntaxTree.GetLineSpan(r.Span).EndLinePosition.Line + 1;
            var mm = memberMetrics.FirstOrDefault(m => m.Item.Key == it.Key); var tm = typeMetrics.FirstOrDefault(t => t.Item.Key == it.Key);
            int uncommented = 0; object ed = null;
            if (q["git"] != "0")
            {
                ed = EdJson(Git().ForSymbol(it, 8));
                if (it.Symbol is INamedTypeSymbol) uncommented = Git().Debtors(an, it, 0, 1).Count;
            }
            return new
            {
                error = (string)null, key = it.Key, display = it.Display, kind = it.Kind, file = it.File, line = it.Line, endLine, doc = Report.Summary(it.Symbol),
                member = mm == null ? null : new { cc = mm.CC, nesting = mm.MaxNesting, loc = mm.Loc, mi = Math.Round(mm.MI) },
                type = tm == null ? null : new { wmc = tm.WMC, ca = tm.Ca, ce = tm.Ce, members = tm.Members, loc = tm.Loc },
                editors = ed, uncommented
            };
        }

        object DebtorsJson(System.Collections.Specialized.NameValueCollection q)
        {
            string err; var it = Pick(q, true, out err); if (it == null) return new { error = err };
            var g = Git(); if (g.Problem != null) return new { error = g.Problem };
            var list = g.Debtors(an, it, ToInt(q["limit"], 40, 0, 500), ToInt(q["commits"], 10, 1, 50));
            return new
            {
                error = (string)null, type = it.Display, total = list.Count,
                items = list.Select(d => new { name = d.Item.Display.Substring(d.Item.Display.LastIndexOf(it.Symbol.Name + ".") >= 0 ? d.Item.Display.LastIndexOf(it.Symbol.Name + ".") + it.Symbol.Name.Length + 1 : 0),
                    kind = d.Item.Kind, line = d.Start, endLine = d.End, file = Path.GetFileName(d.Item.File), editors = d.Editors == null ? null : EdJson(d.Editors) }).ToList()
            };
        }

        object ClonesJson(System.Collections.Specialized.NameValueCollection q)
        {
            var list = Clones.Find(an, ToInt(q["window"], 40, 10, 400), ToInt(q["lines"], 6, 2, 200), q["exact"] == "1", q["gen"] == "1");
            Func<CloneSite, object> s = x => new { member = x.Member, file = x.File, name = Path.GetFileName(x.File), start = x.StartLine, end = x.EndLine };
            return new { error = (string)null, total = list.Count, items = list.Take(300).Select(c => new { tokens = c.Tokens, lines = c.Lines, exact = c.Exact, a = s(c.A), b = s(c.B) }).ToList() };
        }

        object GodJson(System.Collections.Specialized.NameValueCollection q)
        {
            string err; var it = Pick(q, true, out err); if (it == null) return new { error = err };
            double hub; if (!double.TryParse(q["hub"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hub)) hub = 0.25;
            return GodClass.Analyze(an, it, Math.Max(0.05, Math.Min(1.0, hub)));
        }

        object Types(string q)
        {
            EnsureMetrics(); q = (q ?? "").Trim().ToLowerInvariant();
            return typeMetrics.Where(t => q == "" || t.Item.Display.ToLowerInvariant().Contains(q)).OrderByDescending(t => t.Loc).ThenBy(t => t.Item.Display).Take(100)
                .Select(t => new { name = t.Item.Display, kind = t.Item.Kind, loc = t.Loc, members = t.Members, wmc = t.WMC }).ToList();
        }

        object CodeJson(System.Collections.Specialized.NameValueCollection q)
        {
            var file = q["file"] ?? "";
            if (!an.Projects.Any(p => p.SourceFiles.Any(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)))) return new { error = "不是方案內的原始檔。" };
            int a = ToInt(q["from"], 1, 1, int.MaxValue), b = ToInt(q["to"], a, a, a + 400);
            var lines = File.ReadLines(file).Skip(a - 1).Take(b - a + 1).ToList();
            return new { error = (string)null, from = a, lines };
        }

        object Methods(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            return an.Declared().Values.Where(d => d.Symbol is IMethodSymbol && FlowBuilder.BodyOf(d.Symbol) != null
                    && (q == "" || d.Display.ToLowerInvariant().Contains(q)))
                .OrderBy(d => d.Display).Take(80).Select(d => new { name = d.Display, kind = d.Kind, file = Path.GetFileName(d.File), line = d.Line }).ToList();
        }

        object FlowJson(System.Collections.Specialized.NameValueCollection q)
        {
            var name = (q["name"] ?? "").Trim();
            if (name == "") return new { error = "請先選擇一個方法。" };
            var cands = an.Find(name).Where(d => d.Symbol is IMethodSymbol).ToList();
            var exact = cands.Where(d => d.Display == name).ToList();
            if (exact.Count == 1) cands = exact;
            if (cands.Count == 0) return new { error = "找不到方法:" + name };
            if (cands.Count > 1) return new { error = "名稱不唯一,請選擇完整的一個。", candidates = cands.Select(c => c.Display).ToList() };
            var item = cands[0];
            int i; var fo = new FlowOptions
            {
                Expand = int.TryParse(q["expand"], out i) ? Math.Max(0, Math.Min(i, 6)) : 0,
                External = q["ext"] == "1", CallDepth = int.TryParse(q["depth"], out i) ? Math.Max(1, Math.Min(i, 8)) : 3
            };
            var fb = new FlowBuilder(an, fo);
            var flow = fb.Build(item.Symbol, fo.Expand, new HashSet<string>());
            object state = null, data = null;
            if (q["state"] == "1") state = FlowExtras.State(an, item.Symbol.ContainingType, q["member"]);
            if (q["dataflow"] == "1") data = FlowExtras.DataFlow(an, item.Symbol);
            return new
            {
                name = item.Display, file = item.File, line = item.Line, error = (string)null,
                seqMermaid = FlowWriter.Sequence(item.Symbol, flow, false), seqPuml = FlowWriter.Sequence(item.Symbol, flow, true),
                graph = FlowWriter.ActivityGraph(flow), actMermaid = FlowWriter.ActivityMermaid(flow), actPuml = FlowWriter.ActivityPuml(flow),
                tree = FlowExtras.CallTree(an, item, fo.CallDepth), state, data
            };
        }

        void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath;
            var q = Query(ctx);
            // 防止別的網站偷打本機 API:只接受 localhost 的 Host,而且會改動資料的請求必須帶自訂標頭(瀏覽器跨站送不出來)
            var host = ctx.Request.UserHostName ?? "";
            if (!(host.StartsWith("localhost") || host.StartsWith("127.0.0.1"))) { Send(ctx, 403, "text/plain; charset=utf-8", "不允許的來源"); return; }
            var method = ctx.Request.HttpMethod;
            if ((method == "POST" || method == "DELETE") && ctx.Request.Headers["X-CodeAtlas"] != "1") { Send(ctx, 403, Mime[".json"], "{\"error\":\"缺少必要標頭\"}"); return; }

            if (path == "/api/status") { Send(ctx, 200, Mime[".json"], json.Serialize(StatusJson())); return; }
            if (path == "/api/open" && method == "POST") { Send(ctx, 200, Mime[".json"], json.Serialize(OpenApi(ctx))); return; }
            if (path == "/api/browse") { Send(ctx, 200, Mime[".json"], json.Serialize(BrowseJson(q["path"]))); return; }
            if (path == "/api/recent") { Send(ctx, 200, Mime[".json"], json.Serialize(RecentApi(ctx, q))); return; }
            if (an == null)
            {   // 還沒開啟專案:頁面導向「開啟專案」,分析用的 API 回報需要先開啟
                if (path == "/" || path == "/classes" || path == "/flow" || path == "/canvas" || path == "/god" || path == "/clones")
                { ctx.Response.Redirect("/open"); ctx.Response.Close(); return; }
                if (path.StartsWith("/api/") && path != "/api/layout")
                { Send(ctx, 200, Mime[".json"], "{\"error\":\"尚未開啟專案,請先到「開啟專案」頁。\",\"needOpen\":true}"); return; }
            }
            if (path == "/")
            {
                string html; lock (gate) { if (dashboardHtml == null) dashboardHtml = Report.Dashboard(an, true); html = dashboardHtml; }
                Send(ctx, 200, Mime[".html"], html); return;
            }
            if (path == "/api/layout") { LayoutApi(ctx); return; }
            if (path.StartsWith("/api/"))
            {
                object result;
                lock (gate)
                {
                    switch (path)
                    {
                        case "/api/methods": result = Methods(q["q"]); break;
                        case "/api/types": result = Types(q["q"]); break;
                        case "/api/symbol": result = SymbolJson(q); break;
                        case "/api/debtors": result = DebtorsJson(q); break;
                        case "/api/clones": result = ClonesJson(q); break;
                        case "/api/godclass": result = GodJson(q); break;
                        case "/api/code": result = CodeJson(q); break;
                        case "/api/flow": result = FlowJson(q); break;
                        case "/api/meta": result = diagram.Meta(); break;
                        case "/api/outline": result = diagram.Outline(); break;
                        case "/api/diagram":
                            { var o = DiagramOptions.FromQuery(q); result = diagram.ToJson(diagram.Select(o), o); break; }
                        case "/api/mermaid":
                            {
                                var o = DiagramOptions.FromQuery(q); var sel = diagram.Select(o);
                                result = new { text = sel.Error == null ? diagram.ToMermaid(sel, o) : "", error = sel.Error, truncated = sel.Truncated };
                                break;
                            }
                        default: Send(ctx, 404, Mime[".json"], "{\"error\":\"找不到\"}"); return;
                    }
                }
                Send(ctx, 200, Mime[".json"], json.Serialize(result)); return;
            }
            // 靜態檔案:/assets/* 在 Assets,其餘在 Web
            string rel = path.TrimStart('/'), root = Path.Combine(baseDir, "Web");
            if (rel.StartsWith("assets/")) { rel = rel.Substring(7); root = Path.Combine(baseDir, "Assets"); }
            if (rel == "open") rel = "open.html";
            if (rel == "classes") rel = "classes.html";
            if (rel == "flow") rel = "flow.html";
            if (rel == "canvas") rel = "canvas.html";
            if (rel == "god") rel = "god.html";
            if (rel == "clones") rel = "clones.html";
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar) || !File.Exists(full)) { Send(ctx, 404, "text/plain; charset=utf-8", "找不到頁面"); return; }
            string mime;
            if (!Mime.TryGetValue(Path.GetExtension(full), out mime)) mime = "application/octet-stream";
            var bytes = File.ReadAllBytes(full);
            ctx.Response.StatusCode = 200; ctx.Response.ContentType = mime; ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); ctx.Response.Close();
        }

        static void Send(HttpListenerContext ctx, int code, string mime, string body)
        {
            var bytes = new UTF8Encoding(false).GetBytes(body);
            ctx.Response.StatusCode = code; ctx.Response.ContentType = mime; ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length); ctx.Response.Close();
        }
    }
}

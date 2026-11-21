using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeAtlas
{
    /// <summary>
    /// 不依賴 MSBuild / Visual Studio 安裝,直接解析 .sln 與 .csproj,建立 Roslyn Compilation。
    /// 這樣在離線、不能安裝任何東西的電腦上也能執行。
    /// </summary>
    public class LoadedProject
    {
        public string Name;
        public string Path;
        public bool Extra; // 由 --source 加入、用來取代 DLL 的原始碼
        public List<string> SourceFiles = new List<string>();
        public List<string> ReferencePaths = new List<string>();
        public List<string> ProjectReferences = new List<string>();
        public CSharpCompilation Compilation;
    }

    public static class ProjectLoader
    {
        static readonly Regex SlnProject = new Regex(
            "^Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+\\.csproj)\"", RegexOptions.Multiline);

        /// <summary>sources:DLL 名稱 → 原始碼位置(.csproj 或資料夾)。有指定的 DLL 會被原始碼取代,語意分析就能穿透進去。</summary>
        public static List<LoadedProject> Load(string path, Action<string> log, Dictionary<string, string> sources = null)
        {
            path = System.IO.Path.GetFullPath(path);
            var csprojs = new List<string>();
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                foreach (Match m in SlnProject.Matches(File.ReadAllText(path)))
                {
                    var p = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, m.Groups[1].Value.Replace('\\', '/')));
                    if (File.Exists(p)) csprojs.Add(p); else log("找不到專案檔:" + p);
                }
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) csprojs.Add(path);
            else throw new ArgumentException("請指定 .sln 或 .csproj 檔案。");

            var projects = csprojs.Select(p => ParseCsproj(p, log)).ToList();
            var frameworkRefs = FrameworkReferences();

            if (sources != null && sources.Count > 0)
            {
                var extraByName = new Dictionary<string, LoadedProject>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < projects.Count; i++) // 加進來的原始碼專案也可能再參照別的 DLL,所以邊走邊擴
                {
                    var p = projects[i];
                    foreach (var dll in p.ReferencePaths.ToList())
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(dll); string src;
                        if (!sources.TryGetValue(name, out src)) continue;
                        LoadedProject ex;
                        if (!extraByName.TryGetValue(name, out ex))
                        {
                            ex = LoadExtra(name, src, log);
                            if (ex == null) continue;
                            var existing = projects.FirstOrDefault(x => x.Path.Equals(ex.Path, StringComparison.OrdinalIgnoreCase));
                            if (existing != null) ex = existing; else projects.Add(ex);
                            extraByName[name] = ex;
                        }
                        p.ReferencePaths.Remove(dll); p.ProjectReferences.Add(ex.Path);
                        log("以原始碼取代 DLL:" + name + " → " + ex.Path);
                    }
                }
                foreach (var kv in sources)
                    if (!extraByName.ContainsKey(kv.Key)) log("指定的 DLL「" + kv.Key + "」沒有被任何專案參照,已略過。");
            }

            // 先為每個專案建立 Compilation,專案間參照用 CompilationReference
            var byPath = projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var p in projects) Build(p, byPath, frameworkRefs, new HashSet<string>(StringComparer.OrdinalIgnoreCase), log);
            return projects;
        }

        static LoadedProject LoadExtra(string name, string src, Action<string> log)
        {
            var full = System.IO.Path.GetFullPath(src);
            if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            { var lp = ParseCsproj(full, log); lp.Extra = true; return lp; }
            if (Directory.Exists(full))
            {
                var lp = new LoadedProject { Name = name, Path = full, Extra = true };
                lp.SourceFiles = Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories).Where(f =>
                {
                    var first = f.Substring(full.Length + 1).Split('\\', '/')[0];
                    return !first.Equals("bin", StringComparison.OrdinalIgnoreCase) && !first.Equals("obj", StringComparison.OrdinalIgnoreCase);
                }).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                if (lp.SourceFiles.Count == 0) { log("資料夾內沒有 .cs 檔:" + full); return null; }
                return lp;
            }
            log("找不到 " + name + " 的原始碼位置:" + full);
            return null;
        }

        static List<MetadataReference> FrameworkReferences()
        {
            // 目標機器保證有 .NET 4.8:直接使用目前執行環境的框架組件。
            var dir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location);
            var names = new[] { "mscorlib", "System", "System.Core", "System.Data", "System.Xml", "System.Xml.Linq",
                "System.Drawing", "System.Windows.Forms", "System.Configuration", "System.Net.Http", "System.Runtime.Serialization", "System.Numerics" };
            var list = new List<MetadataReference>();
            foreach (var n in names)
            {
                var f = System.IO.Path.Combine(dir, n + ".dll");
                if (File.Exists(f)) list.Add(MetadataReference.CreateFromFile(f));
            }
            return list;
        }

        static LoadedProject ParseCsproj(string csproj, Action<string> log)
        {
            var dir = System.IO.Path.GetDirectoryName(csproj);
            var doc = XDocument.Load(csproj);
            var root = doc.Root;
            var lp = new LoadedProject { Name = System.IO.Path.GetFileNameWithoutExtension(csproj), Path = csproj };
            bool sdk = root.Attribute("Sdk") != null;
            XNamespace ns = root.Name.Namespace;

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sdk)
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(dir.Length + 1);
                    var first = rel.Split('\\', '/')[0];
                    if (first.Equals("bin", StringComparison.OrdinalIgnoreCase) || first.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                    files.Add(f);
                }
                foreach (var rm in root.Descendants(ns + "Compile").Where(e => e.Attribute("Remove") != null))
                {
                    var rp = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, rm.Attribute("Remove").Value));
                    files.Remove(rp);
                }
            }
            foreach (var c in root.Descendants(ns + "Compile").Where(e => e.Attribute("Include") != null))
            {
                var inc = c.Attribute("Include").Value;
                if (inc.Contains("*")) continue; // 萬用字元僅在 SDK 風格由上方列舉處理
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, inc.Replace('\\', '/')));
                if (File.Exists(full)) files.Add(full); else log("缺少原始檔:" + full);
            }
            lp.SourceFiles = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var r in root.Descendants(ns + "Reference"))
            {
                var hint = r.Element(ns + "HintPath");
                if (hint == null) continue;
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, hint.Value.Replace('\\', '/')));
                if (File.Exists(full)) lp.ReferencePaths.Add(full); else log("缺少參考 DLL:" + full);
            }
            // SDK 風格 PackageReference:從本機 NuGet 快取找 DLL(離線可用,找不到就警告)
            var nuget = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            foreach (var pr in root.Descendants(ns + "PackageReference").Where(e => e.Attribute("Include") != null))
            {
                var id = pr.Attribute("Include").Value.ToLowerInvariant();
                var ver = pr.Attribute("Version") != null ? pr.Attribute("Version").Value : (pr.Element(ns + "Version") != null ? pr.Element(ns + "Version").Value : "");
                var pdir = System.IO.Path.Combine(nuget, id, ver, "lib");
                string found = null;
                if (Directory.Exists(pdir))
                    foreach (var tfm in new[] { "net48", "net472", "net471", "net47", "net462", "net461", "net45", "netstandard2.0", "net40" })
                    {
                        var d = System.IO.Path.Combine(pdir, tfm);
                        if (Directory.Exists(d)) { found = d; break; }
                    }
                if (found == null) { log("找不到 NuGet 套件 " + id + " " + ver + "(型別可能顯示為找不到)"); continue; }
                lp.ReferencePaths.AddRange(Directory.GetFiles(found, "*.dll"));
            }
            foreach (var r in root.Descendants(ns + "ProjectReference").Where(e => e.Attribute("Include") != null))
                lp.ProjectReferences.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, r.Attribute("Include").Value.Replace('\\', '/'))));
            return lp;
        }

        static void Build(LoadedProject p, Dictionary<string, LoadedProject> all, List<MetadataReference> fw,
            HashSet<string> visiting, Action<string> log)
        {
            if (p.Compilation != null) return;
            if (!visiting.Add(p.Path)) { log("偵測到專案循環參照:" + p.Name); return; }

            var refs = new List<MetadataReference>(fw);
            refs.AddRange(p.ReferencePaths.Select(f => (MetadataReference)MetadataReference.CreateFromFile(f)));
            foreach (var pr in p.ProjectReferences)
            {
                LoadedProject other;
                if (all.TryGetValue(pr, out other))
                {
                    Build(other, all, fw, visiting, log);
                    if (other.Compilation != null) refs.Add(other.Compilation.ToMetadataReference());
                }
                else log("專案參照不在方案內:" + pr);
            }

            var trees = p.SourceFiles.Select(f =>
                CSharpSyntaxTree.ParseText(File.ReadAllText(f), new CSharpParseOptions(LanguageVersion.Latest), f)).ToList();
            p.Compilation = CSharpCompilation.Create(p.Name, trees, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            visiting.Remove(p.Path);
        }
    }
}

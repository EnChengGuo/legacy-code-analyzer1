# -*- coding: utf-8 -*-
"""把 manual.template.html 組成單一 HTML(圖片與輸出都內嵌),再用 Edge 無頭模式印成 PDF。"""
import base64, html, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
VERSION = sys.argv[1] if len(sys.argv) > 1 else "0.8"
DATE = "2026-11-21"
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

def rd(p):
    with open(os.path.join(HERE, p), "r", encoding="utf-8") as f:
        return f.read()

def marker(x1, y, kind, color="#1f2430", fillhollow="#ffffff"):
    # 在 (x1,y) 畫箭頭/菱形;kind: tri / diamond / diamondh / vee / circle;方向朝右(+x)
    if kind == "tri":
        return f'<polygon points="{x1},{y} {x1-12},{y-6} {x1-12},{y+6}" fill="{fillhollow}" stroke="{color}"/>'
    if kind == "diamondf":
        return f'<polygon points="{x1},{y} {x1+10},{y-6} {x1+20},{y} {x1+10},{y+6}" fill="{color}" stroke="{color}"/>'
    if kind == "diamondh":
        return f'<polygon points="{x1},{y} {x1+10},{y-6} {x1+20},{y} {x1+10},{y+6}" fill="{fillhollow}" stroke="{color}"/>'
    if kind == "vee":
        return f'<polyline points="{x1-10},{y-6} {x1},{y} {x1-10},{y+6}" fill="none" stroke="{color}" stroke-width="1.6"/>'
    if kind == "circle":
        return f'<circle cx="{x1+5}" cy="{y}" r="5" fill="{color}" stroke="{color}"/>'
    return ""

def legend_svg():
    rows = [
        ("繼承", "#1f2937", "", "end", "tri", "子類別 → 父類別,空心三角在父類別那端"),
        ("實作", "#1f2937", "6 4", "end", "tri", "類別 → 介面,虛線加空心三角"),
        ("組合", "#7c3aed", "", "start", "diamondf", "擁有者端是實心菱形:自己建立、同生共死"),
        ("聚合", "#7c3aed", "", "start", "diamondh", "擁有者端是空心菱形:集合或外部傳入"),
        ("關聯", "#7c3aed", "", "end", "vee", "一般持有關係,箭頭指向被持有者"),
        ("依賴", "#9ca3af", "6 4", "end", "vee", "虛線箭頭:只是使用(參數、區域變數、呼叫)"),
        ("巢狀", "#0d9488", "2 4", "start", "circle", "點線,圓點在外層類別:外層 → 內層"),
    ]
    h = 30 * len(rows) + 14
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="640" height="{h}" viewBox="0 0 640 {h}">']
    for i, (name, col, dash, side, kind, desc) in enumerate(rows):
        y = 22 + i * 30
        x0, x1 = 90, 220
        d = f' stroke-dasharray="{dash}"' if dash else ""
        if side == "end":
            out.append(f'<line x1="{x0}" y1="{y}" x2="{x1-(12 if kind=="tri" else 0)}" y2="{y}" stroke="{col}" stroke-width="1.6"{d}/>')
            out.append(marker(x1, y, kind, col))
        else:
            out.append(f'<line x1="{x0+(20 if kind.startswith("diamond") else 10)}" y1="{y}" x2="{x1}" y2="{y}" stroke="{col}" stroke-width="1.6"{d}/>')
            out.append(marker(x0, y, kind, col))
        out.append(f'<text x="10" y="{y+4}" font-size="13" font-weight="bold" fill="#1f2430">{name}</text>')
        out.append(f'<text x="240" y="{y+4}" font-size="12" fill="#374151">{html.escape(desc)}</text>')
    out.append("</svg>")
    return "\n".join(out)

def box(x, y, w, h, text, fill="#eef2ff", stroke="#6366f1", size=12, bold=False):
    lines = text.split("\n")
    s = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="7" fill="{fill}" stroke="{stroke}" stroke-width="1.4"/>'
    for i, ln in enumerate(lines):
        ty = y + h / 2 + (i - (len(lines) - 1) / 2) * (size + 3) + size * 0.35
        s += f'<text x="{x + w/2}" y="{ty}" text-anchor="middle" font-size="{size}" fill="#1f2430"{" font-weight=\"bold\"" if bold else ""}>{html.escape(ln)}</text>'
    return s

def arrow(x1, y1, x2, y2, label="", color="#374151", dash=""):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    s = f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="1.6"{d}/>'
    import math
    a = math.atan2(y2 - y1, x2 - x1)
    p = [(x2, y2), (x2 - 9 * math.cos(a - 0.4), y2 - 9 * math.sin(a - 0.4)), (x2 - 9 * math.cos(a + 0.4), y2 - 9 * math.sin(a + 0.4))]
    s += f'<polygon points="{" ".join(f"{px:.1f},{py:.1f}" for px, py in p)}" fill="{color}"/>'
    if label:
        s += f'<text x="{(x1+x2)/2}" y="{(y1+y2)/2-5}" text-anchor="middle" font-size="10.5" fill="#6b7280">{html.escape(label)}</text>'
    return s

def arch_svg():
    o = ['<svg xmlns="http://www.w3.org/2000/svg" width="660" height="250" viewBox="0 0 660 250">']
    o.append('<text x="6" y="16" font-size="12" font-weight="bold" fill="#6d28d9">Serena 路線</text>')
    o.append(box(6, 26, 100, 44, "AI 用戶端\n(支援 MCP)", "#f3effd", "#7c3aed"))
    o.append(arrow(106, 48, 150, 48, "MCP", "#7c3aed"))
    o.append(box(150, 26, 100, 44, "Serena\n(MCP 伺服器)", "#f3effd", "#7c3aed"))
    o.append(arrow(250, 48, 294, 48, "LSP", "#7c3aed"))
    o.append(box(294, 26, 150, 44, "C# 語言伺服器\n(以 Roslyn 為基礎)", "#f3effd", "#7c3aed"))
    o.append(arrow(444, 48, 488, 48, "", "#7c3aed"))
    o.append(box(488, 26, 100, 44, "C# 原始碼", "#f9fafb", "#9ca3af"))
    o.append('<text x="6" y="100" font-size="11" fill="#b45309">需要:uv、.NET 10 以上、首次使用時下載語言伺服器</text>')
    o.append('<text x="6" y="136" font-size="12" font-weight="bold" fill="#1d4ed8">CodeAtlas 路線(本工具)</text>')
    o.append(box(6, 146, 100, 44, "你\n(瀏覽器 / 命令列)", "#eff6ff", "#2563eb"))
    o.append(arrow(106, 168, 150, 168, "", "#2563eb"))
    o.append(box(150, 146, 160, 44, "CodeAtlas.exe\n(.NET Framework 4.8)", "#eff6ff", "#2563eb"))
    o.append(arrow(310, 168, 354, 168, "直接呼叫", "#2563eb"))
    o.append(box(354, 146, 150, 44, "Roslyn\nMicrosoft.CodeAnalysis", "#eff6ff", "#2563eb"))
    o.append(arrow(504, 168, 548, 168, "", "#2563eb"))
    o.append(box(548, 146, 100, 44, "C# 原始碼", "#f9fafb", "#9ca3af"))
    o.append('<text x="6" y="218" font-size="11" fill="#047857">需要:只有 .NET 4.8;資料夾複製過去就能用。輸出:網頁(給人)+ 短文字(給 AI)</text>')
    o.append('</svg>')
    return "\n".join(o)

def flow_svg():
    o = ['<svg xmlns="http://www.w3.org/2000/svg" width="660" height="230" viewBox="0 0 660 230">']
    o.append(box(6, 20, 96, 46, "方案\n.sln", "#f9fafb", "#9ca3af"))
    o.append(arrow(102, 43, 140, 43))
    o.append(box(140, 8, 150, 70, "CodeAtlas\n(Roslyn 語意分析)", "#eff6ff", "#2563eb", bold=True))
    o.append(arrow(290, 30, 336, 30, "context", "#047857"))
    o.append(box(336, 8, 150, 44, "短摘要\n簽章+位置+關係\n(約 1~2 千字元)", "#e6f6ec", "#16a34a", size=11))
    o.append(arrow(290, 60, 336, 82, "body 起 迄", "#b45309"))
    o.append(box(336, 66, 150, 44, "指定段落的內文\n(只有需要的那幾行)", "#fff4d6", "#d4a017", size=11))
    o.append(arrow(486, 30, 530, 100, "", "#047857"))
    o.append(arrow(486, 88, 530, 108, "", "#b45309"))
    o.append(box(520, 100, 134, 46, "你貼進 Copilot\n對話(或附加檔案)", "#f3effd", "#7c3aed", size=11))
    o.append(arrow(587, 146, 587, 186, "", "#7c3aed"))
    o.append(box(520, 186, 134, 38, "Copilot 回答", "#f3effd", "#7c3aed"))
    o.append(arrow(520, 205, 200, 205, "還需要別的方法?告訴我要讀哪個", "#6b7280", "5 4"))
    o.append(arrow(200, 205, 215, 80, "", "#6b7280", "5 4"))
    o.append('</svg>')
    return "\n".join(o)

def main():
    t = rd("manual.template.html")
    t = t.replace("{{version}}", VERSION).replace("{{date}}", DATE)
    t = t.replace("{{legend-svg}}", legend_svg()).replace("{{arch-svg}}", arch_svg()).replace("{{flow-svg}}", flow_svg())

    def img(m):
        name = m.group(1)
        with open(os.path.join(HERE, "shots", name + ".png"), "rb") as f:
            return "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")
    t = re.sub(r"\{\{img:([\w\-]+)\}\}", img, t)

    def snip(m):
        name = m.group(1)
        with open(os.path.join(HERE, "snips", name + ".txt"), "r", encoding="utf-8") as f:
            s = f.read().replace("D:\\AIBDD\\practise\\CodeAtlas\\samples\\", "").replace("C:\\Users\\88697\\AppData\\Local\\Temp\\GodDemo-git-test\\", "%TEMP%\\GodDemo-git\\")
        return html.escape(s.rstrip())
    t = re.sub(r"\{\{snip:([\w\-]+)\}\}", snip, t)

    left = re.findall(r"\{\{[^}]+\}\}", t)
    if left:
        raise SystemExit("未取代的標記:" + ", ".join(sorted(set(left))))

    out_html = os.path.join(HERE, "manual.html")
    with open(out_html, "w", encoding="utf-8") as f:
        f.write(t)
    out_pdf = os.path.join(HERE, "manual.pdf")
    if os.path.exists(out_pdf):
        os.remove(out_pdf)
    cmd = [EDGE, "--headless=new", "--disable-gpu", "--no-pdf-header-footer", f"--print-to-pdf={out_pdf}", "file:///" + out_html.replace("\\", "/")]
    subprocess.run(cmd, check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=180)
    print("HTML:", out_html, os.path.getsize(out_html))
    print("PDF :", out_pdf, os.path.getsize(out_pdf) if os.path.exists(out_pdf) else "未產生")

if __name__ == "__main__":
    main()

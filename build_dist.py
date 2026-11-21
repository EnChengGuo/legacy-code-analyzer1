# -*- coding: utf-8 -*-
"""組出免安裝發佈資料夾 dist/CodeAtlas 與 zip。zip 內的中文檔名用 UTF-8,避免亂碼。"""
import os, shutil, sys, zipfile

ROOT = os.path.dirname(os.path.abspath(__file__))
VERSION = sys.argv[1] if len(sys.argv) > 1 else "0.8"
BIN = os.path.join(ROOT, "src", "bin", "Release", "net48")
DIST = os.path.join(ROOT, "dist", "CodeAtlas")
ZIP = os.path.join(ROOT, "dist", f"CodeAtlas-{VERSION}.zip")

if os.path.exists(os.path.join(ROOT, "dist")):
    shutil.rmtree(os.path.join(ROOT, "dist"))
os.makedirs(DIST)

def skip(d, names):
    return [n for n in names if n.lower().endswith(".pdb") or n in ("bin", "obj")]

for name in os.listdir(BIN):
    s = os.path.join(BIN, name); t = os.path.join(DIST, name)
    if name.lower().endswith(".pdb"):
        continue
    if os.path.isdir(s): shutil.copytree(s, t, ignore=skip)
    else: shutil.copy2(s, t)
shutil.copytree(os.path.join(ROOT, "samples"), os.path.join(DIST, "samples"), ignore=skip)
shutil.copy2(os.path.join(ROOT, "docs", "CodeAtlas使用說明_v0.9.pdf"), DIST)
shutil.copy2(os.path.join(ROOT, "使用說明.txt"), DIST)

with zipfile.ZipFile(ZIP, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for base, _, files in os.walk(DIST):
        for f in files:
            full = os.path.join(base, f)
            z.write(full, os.path.join("CodeAtlas", os.path.relpath(full, DIST)))
n = sum(len(f) for _, _, f in os.walk(DIST))
size = sum(os.path.getsize(os.path.join(b, f)) for b, _, fs in os.walk(DIST) for f in fs)
print("files:", n, "folder MB:", round(size / 1048576, 1), "zip MB:", round(os.path.getsize(ZIP) / 1048576, 1))

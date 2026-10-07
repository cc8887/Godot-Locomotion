#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Kimodo pipeline motion viewer server (read-only, Python stdlib only).

Routes:
  /                 -> index.html (viewer UI)
  /js/*, /style.css -> static viewer assets
  /api/stages       -> pipeline stage catalog
  /api/list?stage=  -> file listing for a stage (read-only walk under assets/generated)
  /file/<rel>       -> artifact file under assets/generated (path-traversal safe)

The server never writes to the repository; the conversion pipeline stays fully
decoupled and can run without this viewer.
"""

import json
import os
import sys
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PORT = 10015
BIND = "0.0.0.0"
VIEWER_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(VIEWER_DIR, os.pardir, os.pardir))
ASSETS_ROOT = os.path.realpath(os.path.join(PROJECT_ROOT, "assets", "generated"))

STAGES = [
    {
        "id": "kimodo-bvh",
        "name": "① Kimodo BVH（SOMA 骨架 · 30Hz · cm）",
        "dirs": [{"rel": "kimodo_ue/als_regen", "exts": [".bvh"]}],
        "desc": "kimodo 生成的原始 BVH（SOMA 78 关节，位移在 Hips）",
        "expect": "位移轨道应在 Hips，Root 静止",
    },
    {
        "id": "retarget-fbx",
        "name": "② 重定向 FBX（UE5 Manny · 123 条）",
        "dirs": [{"rel": "kimodo_ue/als_regen/fbx", "exts": [".fbx"]}],
        "desc": "Mocara 重定向到 UE5 Manny 骨架（位移仍在 pelvis，未烘焙根运动）",
        "expect": "位移轨道应在 pelvis，root 静止",
    },
    {
        "id": "rootik-fbx",
        "name": "③ 根运动烘焙 FBX（RootIK · 20 条）",
        "dirs": [{"rel": "kimodo_ue/als_regen/fbx_rootik", "exts": [".fbx", ".json"]}],
        "desc": "InAir/Transitions 根运动烘焙（pelvis XY 平面位移提取到 root，垂向保留）",
        "expect": "位移轨道应在 root；pelvis XZ≈0、Y 保留",
    },
    {
        "id": "kimodo-godot",
        "name": "④ Kimodo 最终 Godot 树",
        "dirs": [
            {"rel": "kimodo_als/animations", "exts": [".fbx"], "recursive": True},
            {"rel": "kimodo_als", "exts": [".json"]},
        ],
        "desc": "接入 godot-als 的最终资产树（123 条替换 + 3 条沿用原版）",
        "expect": "与⑤原版基线逐条对比检查差异",
    },
    {
        "id": "als-baseline",
        "name": "⑤ 原版 ALS v4 基线",
        "dirs": [
            {"rel": "als_v4/animations", "exts": [".fbx"], "recursive": True},
            {"rel": "als_v4", "exts": [".json"]},
        ],
        "desc": "ALS 原版导出资产（对照基准）",
        "expect": "作为对照基准",
    },
    {
        "id": "single-sample",
        "name": "⑥ 单样本链与冒烟",
        "dirs": [
            {"rel": "kimodo_ue", "exts": [".fbx", ".bvh", ".json"]},
            {"rel": "kimodo_ue/fbx_smoke", "exts": [".fbx", ".json"]},
        ],
        "desc": "kimodo_ue 根级单样本链（BVH→Manny→RootIK→final）与 fbx_smoke 冒烟产物",
        "expect": "沿单条链逐步检查中间形态",
    },
    {
        "id": "raw-meta",
        "name": "⑦ Raw 源与计划 JSON",
        "dirs": [
            {"rel": "kimodo_als_raw", "exts": [".json"]},
            {"rel": "kimodo_als_raw/raw_sequences", "exts": [".json"]},
            {"rel": "kimodo_ue/als_regen", "exts": [".json"]},
        ],
        "desc": "raw 源输入（SHA1 命名）、六域 source_inputs、批量/校验报告",
        "expect": "只读文本查看",
    },
]

SIDECARS = [".source.json", ".verification.json"]

MIME_TYPES = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".bvh": "text/plain; charset=utf-8",
    ".fbx": "application/octet-stream",
    ".npz": "application/octet-stream",
    ".md": "text/markdown; charset=utf-8",
}


def resolve_assets_path(rel_url_path):
    """Resolve a URL path under ASSETS_ROOT; returns None on traversal attempts."""
    rel = urllib.parse.unquote(rel_url_path).replace("\\", "/").lstrip("/")
    full = os.path.realpath(os.path.join(ASSETS_ROOT, rel))
    if full != ASSETS_ROOT and not full.startswith(ASSETS_ROOT + os.sep):
        return None
    return full


def list_stage_files(stage):
    files = []
    seen = set()
    for d in stage["dirs"]:
        base = resolve_assets_path(d["rel"])
        if not base or not os.path.isdir(base):
            continue
        exts = set(d.get("exts") or [])
        if d.get("recursive"):
            entries = [(r, fs) for r, _, fs in os.walk(base)]
        else:
            entries = [(base, os.listdir(base))]
        for root, names in entries:
            for fname in names:
                fp = os.path.join(root, fname)
                if not os.path.isfile(fp):
                    continue
                ext = os.path.splitext(fname)[1].lower()
                if ext not in exts:
                    continue
                rel = os.path.relpath(fp, ASSETS_ROOT).replace("\\", "/")
                if rel in seen:
                    continue
                seen.add(rel)
                sidecars = [s for s in SIDECARS if os.path.exists(fp + s)]
                files.append({
                    "path": rel,
                    "name": fname,
                    "size": os.path.getsize(fp),
                    "sidecars": sidecars,
                })
    files.sort(key=lambda f: f["path"].lower())
    return files


class Handler(BaseHTTPRequestHandler):
    server_version = "MotionViewer/1.0"

    def _send_json(self, obj, code=200):
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _send_file(self, full_path, mime):
        size = os.path.getsize(full_path)
        self.send_response(200)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(size))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        with open(full_path, "rb") as f:
            while True:
                chunk = f.read(65536)
                if not chunk:
                    break
                self.wfile.write(chunk)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        route = parsed.path
        query = urllib.parse.parse_qs(parsed.query)
        try:
            if route in ("/", "/index.html"):
                self._send_file(os.path.join(VIEWER_DIR, "index.html"),
                                "text/html; charset=utf-8")
            elif route == "/api/stages":
                self._send_json({"stages": STAGES,
                                 "assetsRoot": "assets/generated (read-only)"})
            elif route == "/api/list":
                stage_id = (query.get("stage") or [""])[0]
                stage = next((s for s in STAGES if s["id"] == stage_id), None)
                if stage is None:
                    self._send_json({"error": "unknown stage: %s" % stage_id}, 400)
                    return
                files = list_stage_files(stage)
                self._send_json({"stage": stage_id, "count": len(files),
                                 "files": files})
            elif route.startswith("/file/"):
                full = resolve_assets_path(route[len("/file/"):])
                if not full or not os.path.isfile(full):
                    self._send_json({"error": "not found or outside assets/generated"}, 404)
                    return
                ext = os.path.splitext(full)[1].lower()
                self._send_file(full, MIME_TYPES.get(ext, "application/octet-stream"))
            elif route.startswith("/js/") or route.startswith("/vendor/") or route == "/style.css":
                rel = route.lstrip("/")
                full = os.path.realpath(os.path.join(VIEWER_DIR, rel))
                if not full.startswith(VIEWER_DIR + os.sep) or not os.path.isfile(full):
                    self._send_json({"error": "not found"}, 404)
                    return
                ext = os.path.splitext(full)[1].lower()
                self._send_file(full, MIME_TYPES.get(ext, "application/octet-stream"))
            else:
                self._send_json({"error": "no route: %s" % route}, 404)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, fmt, *args):
        sys.stdout.write("[motion-viewer] %s %s\n" % (self.address_string(), fmt % args))
        sys.stdout.flush()


def main():
    if not os.path.isdir(ASSETS_ROOT):
        print("[motion-viewer] assets root missing: %s" % ASSETS_ROOT)
        sys.exit(1)
    server = ThreadingHTTPServer((BIND, PORT), Handler)
    print("[motion-viewer] serving on http://127.0.0.1:%d" % PORT)
    print("[motion-viewer] assets root: %s" % ASSETS_ROOT)
    print("[motion-viewer] read-only; pipeline artifacts are never modified")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("[motion-viewer] stopped")


if __name__ == "__main__":
    main()

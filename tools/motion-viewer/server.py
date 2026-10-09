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
import hashlib
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
        "name": "① 历史 Kimodo BVH（SOMA · cm）",
        "dirs": [{"rel": "kimodo_ue/als_regen", "exts": [".bvh"]}],
        "desc": "kimodo 生成的原始 BVH（SOMA 78 关节，位移在 Hips）",
        "expect": "位移轨道应在 Hips，Root 静止",
    },
    {
        "id": "retarget-fbx",
        "name": "② 历史重定向 FBX（UE5 Manny）",
        "dirs": [{"rel": "kimodo_ue/als_regen/fbx", "exts": [".fbx"]}],
        "desc": "Mocara 重定向到 UE5 Manny 骨架（位移仍在 pelvis，未烘焙根运动）",
        "expect": "位移轨道应在 pelvis，root 静止",
    },
    {
        "id": "rootik-fbx",
        "name": "③ 历史根运动烘焙 FBX（RootIK）",
        "dirs": [{"rel": "kimodo_ue/als_regen/fbx_rootik", "exts": [".fbx", ".json"]}],
        "desc": "InAir/Transitions 根运动烘焙（pelvis XY 平面位移提取到 root，垂向保留）",
        "expect": "位移轨道应在 root；pelvis XZ≈0、Y 保留",
    },
    {
        "id": "kimodo-godot",
        "name": "④ 当前项目中的 FBX",
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
    {
        "id": "root-ownership-probe",
        "name": "⑧ 水平轨迹分解对照",
        "dirs": [{"rel": "kimodo_ue/root_ownership", "exts": [".fbx", ".json"], "recursive": True}],
        "desc": "独立验证产物：before 保留旧偏移，authored 保留 root 轨迹，inplace 锁定 root；original 为原版 ALS",
        "expect": "使用固定原点和同一相机；pelvis 保留自然残差、垂向及姿态。仅供对照，非活动包",
    },
]

CHAIN_STAGE_IDS = ["kimodo-bvh", "retarget-fbx", "rootik-fbx", "kimodo-godot", "als-baseline"]
CHAIN_SHORT = {
    "kimodo-bvh": "① BVH",
    "retarget-fbx": "② 重定向",
    "rootik-fbx": "③ RootIK",
    "kimodo-godot": "④ 当前项目中的 FBX",
    "als-baseline": "⑤ ALS原版",
}

AUTHORING_PHASES = [('generated','生成候选'), ('master','周期处理／母动作'),
    ('target-retarget','目标重定向 raw'), ('target-root','水平分解 → Root'),
    ('target-runtime','ALS 原地 raw'), ('target-fbx','最终 FBX')]

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
    if 'files' in stage:
        return stage['files']
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


def normalize_asset_key(name):
    """Strip extension and pipeline suffixes so the same clip maps to one key
    across BVH / retarget FBX / RootIK FBX / final tree / ALS baseline."""
    base = os.path.splitext(name)[0]
    for suf in ("_RootIK",):
        if base.endswith(suf):
            base = base[: -len(suf)]
    return base


def resolve_authoring_path(rel):
    """Only exported preview data is mounted, never the whole artifact tree."""
    base = os.path.realpath(os.path.join(PROJECT_ROOT, 'artifacts', 'kimodo_viewer'))
    full = os.path.realpath(os.path.join(base, urllib.parse.unquote(rel).replace('\\', '/')))
    if full == base or not full.startswith(base+os.sep):
        return None
    return full


def authoring_catalog(batch_id=None):
    registry_path = resolve_authoring_path('current.json')
    if not os.path.isfile(registry_path):
        return [], None
    with open(registry_path, encoding='utf-8') as stream:
        registry = json.load(stream)
    selected = batch_id or registry['latestBatch']
    batch = next((b for b in registry['batches'] if b['id']==selected), None)
    if batch is None:
        raise ValueError('Unknown authoring batch: '+selected)
    index_path = resolve_authoring_path(batch['index'])
    if not index_path or not os.path.isfile(index_path):
        raise ValueError('Authoring index is missing or outside the preview root')
    with open(index_path, 'rb') as stream:
        payload = stream.read()
    if hashlib.sha256(payload).hexdigest()!=batch['sha256']:
        raise ValueError('Authoring index changed; register the batch again')
    index = json.loads(payload.decode('utf-8'))
    for entry in index['entries']:
        path = resolve_assets_path(entry['path']) if entry.get('kind')=='fbx' else resolve_authoring_path(entry['path'])
        if not path or not os.path.isfile(path):
            raise ValueError('Authoring preview is missing or outside the preview root')
    return registry['batches'], index


def stage_catalog(batch_id=None):
    """Read the ordinary Demo's active pointer; never silently use legacy assets."""
    stages = json.loads(json.dumps(STAGES))
    pointer = os.path.join(PROJECT_ROOT, 'assets', 'config', 'als_resource_pack.json')
    provenance = None
    stage = next(s for s in stages if s['id'] == 'kimodo-godot')
    if os.path.isfile(pointer):
        with open(pointer, encoding='utf-8-sig') as stream:
            config = json.load(stream)
        prefix = 'res://assets/generated/'
        uri = config['pack']
        if not uri.startswith(prefix):
            raise ValueError('Active pack is outside assets/generated')
        pack_path = resolve_assets_path(uri[len(prefix):])
        if not pack_path or not os.path.isfile(pack_path):
            raise ValueError('Active pack does not exist')
        with open(pack_path, 'rb') as stream:
            payload = stream.read()
        pack = json.loads(payload.decode('utf-8-sig'))
        def root(key):
            value = pack[key]
            if not value.startswith(prefix) or not resolve_assets_path(value[len(prefix):]):
                raise ValueError('Pack root is outside assets/generated: ' + key)
            return value[len(prefix):].rstrip('/')
        asset_root, raw_root = root('assetRoot'), root('rawRoot')
        provenance = dict(pack=uri, sha256=hashlib.sha256(payload).hexdigest(),
                          assetRoot=asset_root, rawRoot=raw_root)
        stage['dirs'] = [dict(rel=asset_root+'/animations', exts=['.fbx'], recursive=True)]
        stage['desc'] = '普通 Demo 当前活动包：' + uri
        stage['name'] = '④ 当前项目中的 FBX'
        raw = next(s for s in stages if s['id'] == 'raw-meta')
        raw['dirs'] = [dict(rel=raw_root+'/raw_sequences', exts=['.json'])]
    else:
        stage['dirs'] = [dict(rel='als_v4/animations', exts=['.fbx'], recursive=True)]
        stage['desc'] = '未配置替换包；普通 Demo 使用原版 ALS'
    _, authoring = authoring_catalog(batch_id)
    if authoring:
        previews = []
        for phase, title in AUTHORING_PHASES:
            files = [e for e in authoring['entries'] if e['phase']==phase]
            previews.append(dict(id='authoring-'+phase, name=title+' · '+authoring['label'],
                dirs=[], files=files, desc='批次 '+authoring['label']+' · '+authoring['status'],
                expect='保留从首帧开始的完整数据与固定原点；逐文件查看源检查状态'))
        stages = previews+stages
    return stages, provenance


def build_chain_index(stages=None):
    if stages is None:
        stages, _ = stage_catalog()
    index = {}
    stage_ids = [s['id'] for s in stages if s['id'].startswith('authoring-')]+CHAIN_STAGE_IDS
    for sid in stage_ids:
        stage = next((s for s in stages if s["id"] == sid), None)
        if not stage:
            continue
        for f in list_stage_files(stage):
            if f.get('kind')!='source-motion' and not f["name"].lower().endswith((".fbx", ".bvh")):
                continue
            key = f['name'] if f.get('kind')=='source-motion' else normalize_asset_key(f["name"])
            if f.get('node', '').endswith(':discovery') and sid in index.get(key, {}):
                continue
            index.setdefault(key, {})[sid] = dict(f)
    out = []
    for key in sorted(index.keys(), key=str.lower):
        entries = index[key]
        out.append({
            "key": key,
            "stages": {sid: entries[sid] for sid in stage_ids if sid in entries},
        })
    return out


def chain_catalog(batch_id=None, *, view='pipeline', compare=None, phase='generated'):
    stages, provenance = stage_catalog(batch_id)
    batches, selected = authoring_catalog(batch_id)
    if view not in ('pipeline','batches'):raise ValueError('Unknown chain view')
    if phase not in dict(AUTHORING_PHASES):raise ValueError('Unknown pipeline phase')
    if not selected:
        columns=[s for s in stages if s['id'] in CHAIN_STAGE_IDS]
    elif view=='pipeline':
        # Historical same-name intermediates cannot stand in for this batch.
        columns=[s for s in stages if s['id'].startswith('authoring-')]
        columns += [dict(s,referenceOnly=True) for s in stages if s['id'] in ('kimodo-godot','als-baseline')]
    else:
        if not compare:raise ValueError('Select a comparison batch')
        _, other=authoring_catalog(compare)
        columns=[]
        for i,batch in enumerate((selected,other)):
            columns.append(dict(id='batch-'+str(i),name=batch['label']+' · '+dict(AUTHORING_PHASES)[phase],
                files=[e for e in batch['entries'] if e['phase']==phase],dirs=[],batch=batch['id']))
    ids=[s['id'] for s in columns];index={}
    for column in columns:
        for entry in list_stage_files(column):
            if entry.get('node','').endswith(':discovery'):continue
            if not entry.get('source') and entry.get('kind')!='source-motion' and not entry['name'].lower().endswith(('.bvh','.fbx')):continue
            key=entry['name'] if entry.get('source') or entry.get('kind')=='source-motion' else normalize_asset_key(entry['name'])
            index.setdefault(key,{})[column['id']]=entry
    # Include batch assets even when the selected stage was not produced.
    if selected:
        for entry in selected['entries']:index.setdefault(entry['name'],{})
        if view=='batches':
            for entry in other['entries']:index.setdefault(entry['name'],{})
    short={s['id']:('独立对照 · ' if s.get('referenceOnly') else '')+
        (dict(AUTHORING_PHASES).get(s['id'].removeprefix('authoring-')) or s.get('name',s['id'])) for s in columns}
    return dict(chainStages=ids,short=short,columns=columns,activePack=provenance,
        selectedBatch=selected and selected['id'],view=view,phase=phase,
        items=[dict(key=k,stages=index[k]) for k in sorted(index,key=str.lower)])


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
                selected = (query.get('batch') or [None])[0]
                stages, provenance = stage_catalog(selected)
                batches, authoring = authoring_catalog(selected)
                self._send_json({"stages": stages, "activePack": provenance,
                                 "batches": batches, "authoringBatch": authoring,
                                 "assetsRoot": "assets/generated + exported authoring previews (read-only)"})
            elif route == "/api/chain":
                self._send_json(chain_catalog((query.get('batch') or [None])[0],
                    view=(query.get('view') or ['pipeline'])[0],compare=(query.get('compare') or [None])[0],
                    phase=(query.get('phase') or ['generated'])[0]))
            elif route == "/api/list":
                stage_id = (query.get("stage") or [""])[0]
                stages, _ = stage_catalog((query.get('batch') or [None])[0])
                stage = next((s for s in stages if s["id"] == stage_id), None)
                if stage is None:
                    self._send_json({"error": "unknown stage: %s" % stage_id}, 400)
                    return
                files = list_stage_files(stage)
                self._send_json({"stage": stage_id, "count": len(files),
                                 "files": files})
            elif route.startswith('/authoring-file/'):
                full = resolve_authoring_path(route[len('/authoring-file/'):])
                if not full or not os.path.isfile(full) or not full.endswith('.motion.json'):
                    self._send_json({'error':'not found or outside authoring previews'}, 404)
                    return
                self._send_file(full, 'application/json; charset=utf-8')
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
        except (OSError, ValueError, KeyError) as error:
            self._send_json({"error": str(error)}, 500)

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

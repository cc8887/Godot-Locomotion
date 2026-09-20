"""Bind every authored Overlay graph and source to the actual compiled UE asset."""
import hashlib
import difflib
import json
import os
import re
import tempfile
from pathlib import Path

import unreal

SOURCE = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
PREFIX = SOURCE + ":OverlayLayer"
MACHINES = {"Overlay States", "Rifle States", "Pistol 1H States", "Pistol 2H States", "Bow States"}


def graph_hash(text):
    text = text.replace("\r", "")
    removed = set()
    pattern = r'''(?ms)^( +)Begin Object Name="[^"]+" ExportPath="/Script/BlueprintGraph\.(K2Node_CallFunction|K2Node_BreakStruct)'([^']+)'"[^\n]*\n(.*?)^\1End Object\n'''
    def prune(match):
        body = match[4]
        pure_break = match[2] == "K2Node_BreakStruct" or ('bDefaultsToPureFunc=True' in body and
                     'MemberName="BreakVector"' in body and "/Script/Engine.KismetMathLibrary" in body)
        if pure_break and "LinkedTo=" not in body and 'PinName="execute"' not in body:
            removed.add(match[3])
            return ""
        return match[0]
    text = re.sub(pattern, prune, text)
    declaration = r'''(?m)^( +)Begin Object Class=/Script/BlueprintGraph\.[^\n]+ExportPath="/Script/BlueprintGraph\.[^']+'([^']+)'"[^\n]*\n\1End Object\n'''
    text = re.sub(declaration, lambda match: "" if match[2] in removed else match[0], text)
    stack, lines = [], []
    for line in text.split("\n"):
        begin = re.search(r'''Begin Object .*ExportPath="/Script/[^']+'([^']+)'"''', line)
        if begin:
            stack.append(begin[1])
        elif re.match(r"^ *End Object$", line) and stack:
            stack.pop()
        node = re.search(r'''Nodes\(\d+\)="/Script/[^']+'([^']+)'"''', line)
        if node:
            path = node[1] if node[1].startswith("/") else stack[-1] + "." + node[1]
            if path in removed:
                continue
            line = re.sub(r"Nodes\(\d+\)=", "Nodes(*)=", line)
        lines.append(line)
    for index, line in enumerate(lines):
        if "CustomProperties Pin " in line and not any(token in line for token in ("LinkedTo=", "ParentPin=", "SubPins=")):
            lines[index] = re.sub(r"PinId=[A-Fa-f0-9]{32},", "PinId=UNLINKED,", line)
    return hashlib.sha256("\n".join(lines).encode("utf-8")).hexdigest()


def export():
    root, output = (Path(os.environ[key]) for key in ("ALS_OVERLAY_REPOSITORY", "ALS_OVERLAY_INPUTS_OUTPUT"))
    if not root.is_absolute() or not output.is_absolute():
        raise ValueError("Absolute repository and output required")
    payload = (root / "assets/config/v4_layering_inputs.json").read_bytes()
    layering = json.loads(payload)
    if layering["schemaVersion"] != 1 or layering["source"] != SOURCE:
        raise RuntimeError("Foreign layering provenance")
    expected = {g["path"]: graph_hash(g["nativeText"]) for g in layering["graphs"]
                if g["path"] == PREFIX or g["path"].startswith(PREFIX + ".")}
    metadata = json.loads(unreal.AlsAnimationGraphLibrary.read_baked_state_machines(unreal.load_asset(SOURCE)))
    machines = [m for m in metadata["bakedMachines"] if m["machineName"] in MACHINES]
    if metadata["source"] != SOURCE or len(machines) != 5 or {m["machineName"] for m in machines} != MACHINES:
        raise RuntimeError("Incomplete baked Overlay machines")
    graphs, changes = {}, []
    with tempfile.TemporaryDirectory(prefix="als-overlay-") as temporary:
        for graph in unreal.ObjectIterator(unreal.EdGraph):
            path = graph.get_path_name()
            if path not in expected:
                continue
            task = unreal.AssetExportTask()
            task.object, task.exporter = graph, unreal.ObjectExporterT3D()
            task.filename = str(Path(temporary) / "graph.t3d")
            task.automated, task.prompt = True, False
            if not unreal.Exporter.run_asset_export_task(task):
                raise RuntimeError("Cannot export " + path)
            data = Path(task.filename).read_bytes()
            text = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
            graphs[path] = graph_hash(text)
            if graphs[path] != expected[path]:
                original = next(g["nativeText"] for g in layering["graphs"] if g["path"] == path)
                differences = list(difflib.unified_diff(original.replace("\r", "").splitlines(),
                                                      text.replace("\r", "").splitlines(), n=1))
                changes.append((len(text), path, differences))
    if graphs != expected or len(graphs) != 82:
        changed = sorted(p for p in expected if graphs.get(p) != expected[p])
        if changes:
            _, path, differences = min(changes)
            unreal.log_warning("OVERLAY_GRAPH_DIFFERENCE " + path)
            for line in differences[:18]:
                unreal.log_warning(line[:700])
        raise RuntimeError("Overlay graph changed since layering export: " + repr(changed[:5]))
    sources = sorted({n["asset"] for n in layering["compiledNodeInventory"]
                      if n["path"].startswith(PREFIX + ".") and n["assetPlayer"]})
    assets = []
    for source in sources:
        asset = unreal.load_asset(source)
        if not isinstance(asset, unreal.AnimSequence):
            raise RuntimeError("Expected Overlay sequence: " + source)
        assets.append({"source": source, "evaluation": json.loads(
            unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))})
    curves = {e["customCurve"] for m in machines for e in m["transitions"] if e["customCurve"]}
    profiles = {e["blendProfile"] for m in machines for e in m["transitions"] if e["blendProfile"]}
    result = {"schemaVersion": 1, "source": SOURCE,
              "graphHashPolicy": "crlf_unlinked_pins_unused_pure_break_nodes",
              "layeringSha256": hashlib.sha256(payload).hexdigest(), "graphHashes": dict(sorted(graphs.items())),
              "bakedMachines": machines,
              "curves": [c for c in metadata["curves"] if c["path"] in curves],
              "blendProfiles": [p for p in metadata["blendProfiles"] if p["path"] in profiles],
              "editorStateNodes": [n for n in metadata["editorStateNodes"] if n["path"].startswith(PREFIX + ".")],
              "assets": assets}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_OVERLAY_INPUTS_OK graphs=" + str(len(graphs)) + " machines=" + str(len(machines)) +
               " assets=" + str(len(assets)) + " assets_saved=0")


export()
if os.environ.get("ALS_OVERLAY_QUIT") == "1":
    _ticks = 0
    def _quit_after_tick(delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_quit_handle)
            unreal.SystemLibrary.quit_editor()
    _quit_handle = unreal.register_slate_post_tick_callback(_quit_after_tick)

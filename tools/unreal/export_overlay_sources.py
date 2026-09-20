"""Export all Overlay raw sources plus native asset and authored-node pose cases."""
import hashlib
import json
import os
import re
import runpy
import struct
from pathlib import Path

import unreal


def export():
    root = Path(os.environ["ALS_OVERLAY_REPOSITORY"])
    if not root.is_absolute():
        raise ValueError("Absolute repository required")
    os.environ["ALS_RAW_SOURCE_PRESERVE_EXISTING"] = "1"
    runpy.run_path(str(root / "tools/unreal/export_movement_source_sequences.py"), run_name="__main__")
    runpy.run_path(str(root / "tools/unreal/export_additive_source_poses.py"), run_name="__main__")
    index_path = Path(os.environ["ALS_MOVEMENT_SOURCE_INDEX"])
    output = Path(os.environ["ALS_OVERLAY_NODE_ORACLE"])
    if not output.is_absolute() or output == index_path:
        raise ValueError("Distinct absolute node oracle required")
    index_bytes = index_path.read_bytes()
    index = json.loads(index_bytes)
    layering_bytes = (root / "assets/config/v4_layering_inputs.json").read_bytes()
    layer = json.loads(layering_bytes)
    graphs = {g["path"]: g["nativeText"].replace("\r", "") for g in layer["graphs"]}
    nodes = sorted((n for n in layer["compiledNodeInventory"] if ":OverlayLayer." in n["path"] and n["assetPlayer"]),
                   key=lambda n: n["compiledNodeIndex"])
    f32 = lambda value: struct.unpack("f", struct.pack("f", value))[0]
    rows = []
    for node in nodes:
        graph_path, name = node["path"].rsplit(".", 1)
        body = re.search(r'^   Begin Object Name="' + re.escape(name) + r'"[^\n]*\n(.*?)^   End Object',
                         graphs[graph_path], re.M | re.S).group(1)
        fixed, sweep = None, False
        if node["evaluator"]:
            pin = next(line for line in body.splitlines() if "CustomProperties Pin " in line and 'PinName="ExplicitTime"' in line)
            if "LinkedTo=" in pin:
                sweep = True
            else:
                fixed = f32(float(re.search(r',DefaultValue="([^"]+)"', pin).group(1)))
        asset = unreal.load_asset(node["asset"])
        for player_seconds, aim_sweep in ((-.1, -.1), (.237, .375), (1.237, 1.1)):
            seconds = f32(aim_sweep if sweep else fixed if fixed is not None else player_seconds)
            pose = json.loads(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(asset, seconds, True, False, False))
            rows.append({"compiledIndex": node["compiledNodeIndex"], "node": node["path"], "source": node["asset"],
                         "playerSeconds": player_seconds, "aimSweepTime": aim_sweep, "pose": pose})
    result = {"schemaVersion": 1, "source": "UE GetAnimationPose; authored Overlay evaluator pins; caller-owned player seconds",
              "sourceIndexSha256": hashlib.sha256(index_bytes).hexdigest(),
              "layeringSha256": hashlib.sha256(layering_bytes).hexdigest(), "rows": rows}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8")
    unreal.log("ALS_OVERLAY_NODE_POSES_OK nodes=" + str(len(nodes)) + " poses=" + str(len(rows)) + " assets_saved=0")


export()

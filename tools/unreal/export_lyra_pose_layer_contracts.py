"""Read current Lyra pose-layer graphs and verify their retained source snapshot."""

import hashlib
import json
import os
import re
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
asset = "/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase"
snapshot = Path(unreal.Paths.project_saved_dir()) / "BP2DSL/Exports/20260913-134530-535215/Lyra"
result = unreal.AnimBP2FPPythonBridge.export_anim_blueprint_to_text(asset)
if not result.success:
    raise RuntimeError("Current Lyra pose graph export failed: " + str(result.message))
current = str(result.dsl_text).replace("\r\n", "\n")
archived = (snapshot / "AnimBP2Lisp/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase.animlang").read_text(encoding="utf-8").replace("\r\n", "\n")


def layer(text, name):
    match = re.search(r':graph-name "' + re.escape(name) + '"', text)
    if match is None:
        raise RuntimeError("Missing source animation layer: " + name)
    start = text.rfind("(animation-layer", 0, match.start())
    depth, quoted, escaped = 0, False, False
    for index in range(start, len(text)):
        char = text[index]
        if quoted:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                quoted = False
        elif char == '"':
            quoted = True
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return text[start:index + 1].strip()
    raise RuntimeError("Unbalanced source layer: " + name)


graphs = {}
for name in ("FullBodyAdditives", "LeftHandPose_OverrideState", "FullBody_SkeletalControls"):
    live = layer(current, name)
    old = layer(archived, name)
    if live != old:
        raise RuntimeError("Current animation layer differs from snapshot: " + name)
    graphs[name] = {"sha256": hashlib.sha256(live.encode("utf-8")).hexdigest(), "text": live}

additive = graphs["FullBodyAdditives"]["text"]
if ('("AirIdentity" -> "LandRecovery" :priority 1 :rule-graph "(transition-cond false)")' not in additive or
        ':initial "Identity"' not in additive or
        ':name "AirIdentity"' not in additive):
    raise RuntimeError("Original additive machine no longer has its disabled landing edge")
functions = {}
for name in ("SetLeftHandPoseOverrideWeight", "UpdateSkelControlData"):
    exported = unreal.AnimBP2FPPythonBridge.export_event_graph_to_text(asset, name, False, True)
    if not exported.success:
        raise RuntimeError("Current pose-layer function export failed: " + name)
    live = str(exported.dsl_text).replace("\r\n", "\n").strip()
    old = (snapshot / ("BlueprintLisp/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase/Function/" + name + ".bplisp")).read_text(encoding="utf-8").replace("\r\n", "\n").strip()
    if live != old:
        raise RuntimeError("Current pose-layer function differs from snapshot: " + name)
    functions[name] = {"sha256": hashlib.sha256(live.encode("utf-8")).hexdigest(), "text": live}


def package_hash(path):
    package = Path(unreal.Paths.project_content_dir()) / (path.removeprefix("/Game/").split(".")[0] + ".uasset")
    return hashlib.sha256(package.read_bytes()).hexdigest()


inventory_path = root / "linked_layer_inventory.json"
inventory_bytes = inventory_path.read_bytes()
inventory = json.loads(inventory_bytes)
profiles = {}
for name, row in inventory["classes"].items():
    cdo = unreal.get_default_object(unreal.load_class(None, row["class"]))
    enabled = bool(cdo.get_editor_property("EnableLeftHandPoseOverride"))
    pose = cdo.get_editor_property("LeftHandPose_Override")
    pose_path = pose.get_path_name() if pose else None
    if enabled != row["scalars"]["EnableLeftHandPoseOverride"] or pose_path != row["assets"]["LeftHandPose_Override"]:
        raise RuntimeError("Current left hand CDO differs: " + name)
    profiles[name] = {"enabled": enabled, "source": pose_path, "class": row["class"],
                      "classUassetSha256": package_hash(row["class"])}

payload = {"schemaVersion": 1, "baseAsset": asset,
           "baseUassetSha256": package_hash(asset),
           "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "graphs": graphs, "functions": functions,
           "additive": {"initial": "Identity", "air": "AirIdentity", "landingEdgeEnabled": False},
           "leftHand": {"curve": "DisableLeftHandPoseOverride", "mask": "LeftFingersMask",
                        "meshSpaceRotation": False, "evaluatorTimeSeconds": 0.0,
                        "profiles": profiles}}
output = root / "pose_layer_contracts.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing pose-layer contract differs; inspect before replacing it")
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_POSE_LAYER_CONTRACT_OK graphs=3 functions=2 profiles=9 assets_saved=0")

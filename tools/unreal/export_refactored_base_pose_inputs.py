"""Read original Layering SequenceEvaluator sources and independent native pose references."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repository = Path(__file__).parents[2]
output = Path(os.environ["ALS_BASE_POSE_INPUTS"])
reference = Path(os.environ["ALS_BASE_POSE_REFERENCE"])
if not output.is_absolute() or not reference.is_absolute() or output == reference:
    raise ValueError("Distinct absolute output paths required")
inventory_bytes = (repository / "assets/config/refactored_layering_inventory.json").read_bytes()
inventory = json.loads(inventory_bytes)
layer = next(row for row in inventory["blueprints"] if row["source"].endswith("/AB_Als_Layering.AB_Als_Layering"))
nodes = [node for node in layer["nodes"] if node["class"] == "AnimGraphNode_SequenceEvaluator" and node["compiledNodeIndex"] >= 0]
if len(nodes) != 2:
    raise ValueError("Changed base pose source closure")
def decode(value):
    return json.loads(value, parse_int=lambda token: -0.0 if token == "-0" else int(token))
sequences, skeletons, references = [], {}, []
for node in nodes:
    asset = unreal.load_asset(node["runtime"]["sequence"])
    if not isinstance(asset, unreal.AnimSequence):
        raise ValueError("Missing base pose sequence")
    raw = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset))
    evaluation = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
    curves = decode(unreal.AlsSourceAnimationLibrary.read_source_float_curves(asset))
    skeleton = asset.get_editor_property("skeleton")
    skeletons[skeleton.get_path_name()] = {"metadata": decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))}
    sequences.append({"raw": raw, "evaluation": evaluation, "curves": curves})
    samples = []
    for fraction in (0, .137, .419, .773, 1):
        time = raw["playLength"] * fraction
        for label, retarget, extract, ignore in (("raw", False, False, True), ("retargeted", True, False, True),
                                                ("asset_root_lock", True, False, False), ("extract_root_lock", True, True, False)):
            pose = decode(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(asset, time, retarget, extract, ignore))
            pose.update(context=label, requestedTime=time)
            samples.append(pose)
    references.append({"source": asset.get_path_name(), "samples": samples})
def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
write(output, {"schemaVersion": 1, "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
               "sequences": sequences, "skeletons": skeletons})
write(reference, {"schemaVersion": 1, "sourceSha256": hashlib.sha256(output.read_bytes()).hexdigest(), "sequences": references})
unreal.log("ALS_REFACTORED_BASE_POSES_OK sources=2 samples=40 assets_saved=0")

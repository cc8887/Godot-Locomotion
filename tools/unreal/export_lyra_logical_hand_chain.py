"""Run the actual inherited hand-control nodes against complete ALS 81-bone poses."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
requests_path = Path(os.environ["LYRA_HAND_REQUESTS"])
if not root.is_absolute() or not requests_path.is_absolute():
    raise ValueError("Absolute source/output paths required")
logical = root / "logical_controls"
sha = lambda data: hashlib.sha256(data).hexdigest()
request_bytes = requests_path.read_bytes()
requests = json.loads(request_bytes)
calibration_bytes = (logical / "calibration.json").read_bytes()
catalog_bytes = (logical / "catalog.json").read_bytes()
calibration = json.loads(calibration_bytes)
if (requests["schemaVersion"] != 1 or len(requests["rows"]) != 48 or
        requests["calibrationSha256"] != sha(calibration_bytes) or requests["catalogSha256"] != sha(catalog_bytes)):
    raise ValueError("Stale logical hand-chain requests")
inventory = json.loads((root / "linked_layer_inventory.json").read_bytes())
defaults = json.loads((root / "skeletal_control_defaults.json").read_bytes())
root_defaults = json.loads((root / "root_yaw_defaults.json").read_bytes())
content = Path(unreal.Paths.project_content_dir())

def package_hash(path):
    return sha((content / (path.split(".", 1)[0].removeprefix("/Game/") + ".uasset")).read_bytes())

assets = calibration["assetSha256"].copy()
for path in [root_defaults["class"], *[v["class"] for v in inventory["classes"].values()]]:
    assets[path] = package_hash(path)
for path, digest in assets.items():
    if package_hash(path) != digest:
        raise ValueError("Source package changed: " + path)
basis = calibration["calibration"]
source_mesh = unreal.load_asset(basis["sourceMesh"])
target_mesh = unreal.load_asset(basis["targetMesh"])
q = basis["handBasis"]["rotation"]
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source_mesh.get_editor_property("skeleton"), target_mesh.get_editor_property("skeleton"), unreal.Quat(*q))
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
if layout["logicalBoneNames"] != calibration["layout"]["logicalBoneNames"]:
    raise ValueError("Extended native skeleton differs")
settings = json.loads(unreal.AlsLyraControlRigLibrary.read_root_yaw_node_settings(unreal.load_class(None, root_defaults["class"])))
if len(settings["nodes"]) != 1:
    raise ValueError("Unexpected RotateRootBone inventory")
rows = []
for row in requests["rows"]:
    profile = row["profile"]
    layer_class = unreal.load_class(None, inventory["classes"][profile]["class"])
    disabled = bool(unreal.get_default_object(layer_class).get_editor_property("DisableHandIK"))
    retarget_alpha = min(1, max(0, 1 - row["retargetDisable"]))
    right_alpha = min(1, max(0, (0 if disabled else 1) - row["rightDisable"]))
    left_alpha = min(1, max(0, (0 if disabled else 1) - row["leftDisable"]))
    result = json.loads(unreal.AlsHandControlLibrary.read_logical_hand_chain(skeleton, layer_class,
        json.dumps({"pose": row["pose"]}, separators=(",", ":")), defaults["profiles"][profile]["Hand FKWeight"],
        retarget_alpha, right_alpha, left_alpha))
    if len(result["stages"]) != 4 or any(len(stage) != 81 for stage in result["stages"]):
        raise ValueError("Incomplete native hand chain")
    rows.append({**row, "retargetAlpha": retarget_alpha, "rightAlpha": right_alpha, "leftAlpha": left_alpha, "native": result})
if any(package_hash(path) != digest for path, digest in assets.items()):
    raise ValueError("Native hand chain changed a package")
payload = {"schemaVersion": 1, "calibrationSha256": sha(calibration_bytes), "catalogSha256": sha(catalog_bytes),
           "requestsSha256": sha(request_bytes), "assetSha256": assets, "rootYawSettings": settings,
           "operator": "Inherited HandIKRetargeting -> CopyBone -> RightTwoBoneIK -> LeftTwoBoneIK; one FCSPose", "rows": rows}
path = logical / "hand_chain_native.json"
if path.exists():
    if json.loads(path.read_text(encoding="utf-8")) != payload:
        raise ValueError("Existing logical hand-chain oracle differs")
else:
    path.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_LOGICAL_HAND_CHAIN_NATIVE_OK cases=48 logical=81 rootNodes=1 assets_saved=0")

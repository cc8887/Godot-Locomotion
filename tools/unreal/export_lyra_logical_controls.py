"""Export transient ALS 69 raw / 81 logical poses with animated weapon controls.

Existing retargeted skin tracks are copied, never rebaked or saved. Weapon local
channels are calibrated from the actual retarget poses; missing VBs are generated
by the real UE raw animation data model before interpolation/additive conversion.
"""
import hashlib
import json
import math
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute LYRA_OUTPUT_ROOT required")
destination = root / "logical_controls"
destination.mkdir(exist_ok=True)
(destination / "clips").mkdir(exist_ok=True)
source_bytes = (root / "weapon_space_sources.json").read_bytes()
source_data = json.loads(source_bytes)
dependencies = {"weapon_space_sources.json": hashlib.sha256(source_bytes).hexdigest()}


def read(name):
    data = (root / name).read_bytes()
    dependencies[name] = hashlib.sha256(data).hexdigest()
    return json.loads(data)


def package_hash(path):
    package = path.split(".", 1)[0]
    if not package.startswith("/Game/"):
        raise ValueError("Expected project asset")
    return hashlib.sha256((Path(unreal.Paths.project_content_dir()) / (package[6:] + ".uasset")).read_bytes()).hexdigest()


def save(path, payload):
    if path.exists():
        if json.loads(path.read_text(encoding="utf-8")) != payload:
            raise RuntimeError("Existing logical control export differs: " + str(path))
    else:
        path.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pose(sequence, time, retarget, evaluated=False):
    reader = (unreal.AlsSourceAnimationLibrary.read_raw_animation_pose if evaluated else
              unreal.AlsSourceAnimationLibrary.read_raw_bone_pose)
    data = json.loads(reader(sequence, time, retarget, False, not retarget))
    atoms = data["pose"]
    if any(not math.isfinite(value) for atom in atoms for values in atom.values() for value in values):
        raise RuntimeError("Nonfinite native logical pose")
    return atoms


ordinary = [(name, row) for name in ("unarmed_catalog.json", "unarmed_aux_catalog.json",
            "unarmed_remaining_catalog.json", "unarmed_crouch_transitions_catalog.json",
            "pistol_catalog.json", "rifle_catalog.json") for row in read(name)["clips"]]
aim = [(profile, row) for profile in ("unarmed", "pistol", "rifle")
       for row in read(profile + "_aim_samples_catalog.json")["clips"]]
if len(ordinary) != 189 or len(aim) != 45:
    raise RuntimeError("Incomplete current three-layer closure")
base_catalog = read("unarmed_catalog.json")
defaults = read("skeletal_control_defaults.json")
assets = source_data["assetSha256"].copy()
for path in (base_catalog["sourceMesh"], base_catalog["targetMesh"], base_catalog["ikRetargeter"],
             defaults["skeletons"]["source"]["asset"], defaults["skeletons"]["target"]["asset"]):
    assets[path] = package_hash(path)
for path, sha in assets.items():
    if package_hash(path) != sha:
        raise RuntimeError("Source or target asset changed: " + path)
source_mesh = unreal.load_asset(base_catalog["sourceMesh"])
target_mesh = unreal.load_asset(base_catalog["targetMesh"])
retargeter = unreal.load_asset(base_catalog["ikRetargeter"])
calibration = json.loads(unreal.AlsLyraControlRigLibrary.read_hand_basis_calibration(
    source_mesh, target_mesh, retargeter))
q = calibration["handBasis"]["rotation"]
basis = unreal.Quat(*q)
source_skeleton = source_mesh.get_editor_property("skeleton")
target_skeleton = target_mesh.get_editor_property("skeleton")
extended = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(source_skeleton, target_skeleton, basis)
if extended is None:
    raise RuntimeError("Could not create transient ALS control Skeleton")
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(extended))
layout["source"] = "ALS68Skin69Raw81Logical-v1"
if (len(layout["rawBoneNames"]) != 69 or len(layout["logicalBoneNames"]) != 81 or
        layout["rawBoneNames"][:68] != defaults["skeletons"]["target"]["layout"]["rawBoneNames"] or
        layout["rawBoneNames"][68] != "weapon_r" or layout["logicalBoneNames"][80] != "VB IK_Hand_L_weaponSpace"):
    raise RuntimeError("Invalid ALS control Skeleton layout")
calibration_payload = {"schemaVersion": 1, "dependencySha256": dependencies.copy(),
                       "assetSha256": assets, "calibration": calibration, "layout": layout,
                       "skinLogicalIndices": list(range(68)), "weaponLogicalIndex": 68,
                       "weaponSpaceVirtualIndex": 80,
                       "policy": "ALS adapter: rotate attached local weapon channels into target hand basis; centimeter offsets and skin tracks preserved"}
calibration_sha = save(destination / "calibration.json", calibration_payload)
entries, native = [], []
max_skin_position = max_skin_quaternion = max_skin_scale = 0


def export(category, row, base=None):
    global max_skin_position, max_skin_quaternion, max_skin_scale
    source, target = unreal.load_asset(row["source"]), unreal.load_asset(row["target"])
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(source, target, extended, basis, base)
    if sequence is None:
        raise RuntimeError("Could not create transient control sequence: " + row["target"])
    keys = json.loads(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequence))
    keys["source"] = row["target"]
    keys["skeletonSource"] = layout["source"]
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    metadata["retargetTransformsSourceName"] = row["target"].split(".", 1)[0]
    if metadata["baseAsset"] is not None:
        metadata["baseAsset"] = row["metadata"]["baseAsset"]
    slot = row.get("slot", "aim_" + category + "_" + str(row.get("index")))
    data = {"schemaVersion": 1, "calibrationSha256": calibration_sha,
            "source": row["source"], "target": row["target"], "metadata": metadata, "raw": keys}
    relative = "clips/" + slot + ".json"
    sha = save(destination / relative, data)
    entry = {"slot": slot, "category": category, "source": row["source"], "target": row["target"],
             "file": relative, "sha256": sha, "keyCount": keys["sampledKeyCount"],
             "playLength": keys["playLength"], "additive": "index" in row}
    if entry["additive"]:
        entry["point"] = row["point"]
        entry["sampleIndex"] = row["index"]
    entries.append(entry)
    for ratio in (0, 0.37, 0.75, 1):
        time = keys["playLength"] * ratio
        raw = pose(sequence, time, False)
        evaluated = entry["additive"]
        output = pose(sequence, time, True, evaluated)
        old_output = pose(target, time, True, evaluated)
        if len(raw) != 81 or len(output) != 81 or len(old_output) != 79:
            raise RuntimeError("Native oracle returned wrong pose layout")
        for a, b in zip(output[:68], old_output[:68]):
            max_skin_position = max(max_skin_position, math.dist(a["position"], b["position"]))
            sign = -1 if sum(x * y for x, y in zip(a["rotation"], b["rotation"])) < 0 else 1
            max_skin_quaternion = max(max_skin_quaternion, math.dist(a["rotation"], [v * sign for v in b["rotation"]]))
            max_skin_scale = max(max_skin_scale, math.dist(a["scale"], b["scale"]))
        native.append({"slot": slot, "seconds": time, "raw": raw, "output": output})
    unreal.log("LYRA_LOGICAL_CONTROL_CLIP " + slot + " keys=" + str(keys["sampledKeyCount"]))
    return sequence


for category, row in ordinary:
    export(category, row)
for profile in ("unarmed", "pistol", "rifle"):
    samples = [row for name, row in aim if name == profile]
    center_row = next(row for row in samples if row["point"][:2] == [0, 0])
    center = export(profile, center_row)
    for row in samples:
        if row is not center_row:
            export(profile, row, center)
if len(entries) != 234 or len(native) != 936 or max(max_skin_position, max_skin_quaternion, max_skin_scale) > 1e-12:
    raise RuntimeError("Extended controls changed old skin output: " + str([max_skin_position, max_skin_quaternion, max_skin_scale]))
if any(package_hash(path) != sha for path, sha in assets.items()):
    raise RuntimeError("Native assets changed")
catalog = {"schemaVersion": 1, "calibrationSha256": calibration_sha, "entries": entries,
           "skinPreservation": {"positionCm": max_skin_position, "quaternion": max_skin_quaternion, "scale": max_skin_scale}}
catalog_sha = save(destination / "catalog.json", catalog)
save(destination / "sampling.json", {"schemaVersion": 1, "calibrationSha256": calibration_sha,
                                    "frameTimeRounding": "RoundSubframe",
                                    "native": json.loads(unreal.AlsSourceAnimationLibrary.read_raw_sampling_cases())})
save(destination / "native.json", {"schemaVersion": 1, "catalogSha256": catalog_sha,
                                   "calibrationSha256": calibration_sha, "rows": native})
unreal.log("LYRA_LOGICAL_CONTROLS_OK ordinary=189 additive=45 raw=69 logical=81 skin=68 native=936 skinError=0 assets_saved=0")

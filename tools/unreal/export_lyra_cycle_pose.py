"""Evaluate original Cycle LayeredBoneBlend against transient ALS81 source leaves.

Clock inputs are the existing native root-update fixture. This samples every
active frame but does not evaluate warps or generate RootMotion attributes.
"""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("cycle_layer_graph.json", "cycle_layer_requests.json", "cycle_layer_native_bits.json",
         "logical_controls/catalog.json", "logical_controls/calibration.json",
         "logical_controls/curve_bank.json", "unarmed_layer_masks.json", "linked_layer_inventory.json")
files = {name: (root / name).read_bytes() for name in names}
graph, requests, clock, catalog, calibration, curves, masks, inventory = map(json.loads, files.values())
if clock["requestSha256"] != sha(files["cycle_layer_requests.json"]):
    raise ValueError("Stale native Cycle source times")
content = Path(unreal.Paths.project_content_dir())
packages = dict(clock["assetSha256"])
def package_hash(path):
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())
def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing Cycle pose resource differs: " + name)
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
if any(package_hash(path) != digest for path, digest in packages.items()):
    raise ValueError("Changed native Cycle provenance")
basis = calibration["calibration"]
source_mesh = unreal.load_asset(basis["sourceMesh"])
target_mesh = unreal.load_asset(basis["targetMesh"])
rotation = unreal.Quat(*basis["handBasis"]["rotation"])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source_mesh.get_editor_property("skeleton"), target_mesh.get_editor_property("skeleton"), rotation)
if json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))["logicalBoneNames"] != calibration["layout"]["logicalBoneNames"]:
    raise ValueError("Native ALS81 layout changed")
assets = [a["path"] for a in clock["assets"]]
entries = {entry["target"]: entry for entry in catalog["entries"]}
sequences = []
for path in assets:
    entry = entries[path]
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(entry["source"]), unreal.load_asset(path), skeleton, rotation, None)
    if sequence is None:
        raise ValueError("Missing transient Cycle sequence: " + path)
    sequences.append(sequence)
traces, policies, probes = [], {}, []
for trace, authored in zip(clock["traces"], requests["traces"], strict=True):
    profile = trace["profile"]
    frames = []
    for index, (frame, request) in enumerate(zip(trace["frames"], authored["frames"], strict=True)):
        if not frame["active"]:
            continue
        child = frame["hipFireAsset"] or authored["bindings"]["Aim_HipFirePose"]
        frames.append({"frame": index, "base": assets.index(frame["asset"]), "child": assets.index(child),
                       "time": frame["time"], "childTime": frame["hipFireTime"], "weight": frame["blendWeight"]})
    native = json.loads(unreal.AlsLyraGraphLibrary.read_cycle_layer_pose_trace(
        skeleton, unreal.load_class(None, inventory["classes"][profile]["class"]), sequences,
        json.dumps({"frames": frames, "includeProbes": not traces}, separators=(",", ":"))))
    if len(native["rows"]) != len(frames) or native["generatedRootMotion"]:
        raise ValueError("Incomplete native pre-warp pose trace")
    attributes = native["rows"][0]["output"]["attributes"]
    policy = {"mask": native["mask"], "curveBindings": native["curveBindings"],
              "attributes": [{k: value[k] for k in ("name", "bone", "type", "namespace", "blend")} for value in attributes]}
    if profile in policies and policies[profile] != policy:
        raise ValueError("Cycle pose policy changed between frequencies")
    policies[profile] = policy
    if "probes" in native:
        probes = native["probes"]
    traces.append({"profile": profile, "hz": trace["hz"], "rows": native["rows"]})
if len(traces) != 9 or sum(len(t["rows"]) for t in traces) != 3528:
    raise ValueError("Incomplete Cycle pose fixture")
if any(package_hash(path) != digest for path, digest in packages.items()):
    raise ValueError("Cycle pose extraction changed a source asset")
dependencies = {name: sha(data) for name, data in files.items()}
policy_sha = save("cycle_layer_pose_policy.json", {"schemaVersion": 1, "dependencies": dependencies,
    "policies": policies, "stage": "AuthoredPreWarp", "generatedRootMotion": False})
save("cycle_layer_pose_native_v2.json", {"schemaVersion": 1, "dependencies": dependencies,
    "policySha256": policy_sha, "assetSha256": packages, "traces": traces, "probes": probes,
    "scope": "Actual original Cycle LayeredBoneBlend Evaluate with name-mapped transient ALS81 mask and actual raw GetAnimationPose sequence leaves at native Sync times. Authored curves/integer attributes; generated RootMotion disabled, no warp or complete Main evaluation."})
unreal.log("LYRA_CYCLE_POSE_NATIVE_OK traces=9 frames=3528 logical=81 sequences=42 assets_saved=0")

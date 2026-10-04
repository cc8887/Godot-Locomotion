"""Original Cycle LayeredBlend -> local/CS -> actual OrientationWarping node.

Locomotion/component/alpha inputs are explicit controlled node inputs, not a
new complete Main update. Source times/deltas remain real common Sync outputs.
"""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("cycle_layer_graph.json", "cycle_layer_requests.json", "cycle_layer_native_bits.json",
         "logical_controls/catalog.json", "logical_controls/calibration.json", "root_motion_policy.json",
         "cycle_layer_pose_policy.json", "linked_layer_inventory.json")
files = {name: (root / name).read_bytes() for name in names}
graph, requests, clock, catalog, calibration, root_policy, pose_policy, inventory = map(json.loads, files.values())
content = Path(unreal.Paths.project_content_dir())
packages = clock["assetSha256"]
def check_packages():
    for path, digest in packages.items():
        if sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes()) != digest:
            raise ValueError("Changed Orientation provenance: " + path)
def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing Orientation resource differs: " + name)
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
check_packages()
basis = calibration["calibration"]
rotation = unreal.Quat(*basis["handBasis"]["rotation"])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis["sourceMesh"]).get_editor_property("skeleton"),
    unreal.load_asset(basis["targetMesh"]).get_editor_property("skeleton"), rotation)
assets = [a["path"] for a in clock["assets"]]
entries = {entry["target"]: entry for entry in catalog["entries"]}
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
    unreal.load_asset(entries[path]["source"]), unreal.load_asset(path), skeleton, rotation, None) for path in assets]
if any(sequence is None for sequence in sequences):
    raise ValueError("Missing transient Orientation source")
traces, controlled, policies = [], [], {}
angles = [0, 35, 88, -45, 110, -170, 179, -179, 15, -80, 360, 720]
for trace, authored in zip(clock["traces"], requests["traces"], strict=True):
    frames, controls = [], []
    previous_index = -1
    for index, (frame, request) in enumerate(zip(trace["frames"], authored["frames"], strict=True)):
        if not frame["active"]:
            controls.append(None)
            continue
        angle = angles[(index // 7) % len(angles)]
        yaw = [0, 45, -90, 170][(index // 23) % 4]
        half = math.radians(yaw) * .5
        vector = [math.cos(math.radians(angle)) * 300, math.sin(math.radians(angle)) * 300, 29] if (index // 11) % 2 else [0, 0, 0]
        alpha = [1, .35, 0, 1, .8][(index // 31) % 5]
        control = {"delta": request["delta"], "angle": angle, "direction": vector,
                   "relativeRotation": [0, 0, math.sin(half), math.cos(half)], "alpha": alpha,
                   "weight": frame["cycleWeight"], "ticks": index - previous_index,
                   "counter": index, "reinitialize": request["reinitialize"]}
        controls.append(control)
        child = frame["hipFireAsset"] or authored["bindings"]["Aim_HipFirePose"]
        frames.append({"frame": index, "base": assets.index(frame["asset"]), "child": assets.index(child),
                       "time": frame["time"], "childTime": frame["hipFireTime"], "weight": frame["blendWeight"],
                       "previous": frame["previous"], "delta": frame["delta"],
                       "childPrevious": frame["hipFirePrevious"], "childDelta": frame["hipFireDelta"], "orientation": control})
        previous_index = index
    native = json.loads(unreal.AlsLyraGraphLibrary.read_cycle_layer_pose_trace(
        skeleton, unreal.load_class(None, inventory["classes"][trace["profile"]]["class"]), sequences,
        json.dumps({"frames": frames, "generatedRootMotion": True, "orientation": True}, separators=(",", ":"))))
    if len(native["rows"]) != len(frames):
        raise ValueError("Incomplete actual Orientation evaluation")
    policy = native["orientationPolicy"]
    if trace["profile"] in policies and policies[trace["profile"]] != policy:
        raise ValueError("Orientation mapping changed across Hz")
    policies[trace["profile"]] = policy
    traces.append({"profile": trace["profile"], "hz": trace["hz"], "rows": native["rows"]})
    controlled.append({"profile": trace["profile"], "hz": trace["hz"], "frames": controls})
check_packages()
dependencies = {name: sha(data) for name, data in files.items()}
policy_sha = save("orientation_policy.json", {"schemaVersion": 1, "dependencies": dependencies, "policies": policies})
requests_sha = save("orientation_requests.json", {"schemaVersion": 1, "dependencies": dependencies, "traces": controlled})
save("orientation_native_v2.json", {"schemaVersion": 1, "dependencies": dependencies, "policySha256": policy_sha,
    "requestsSha256": requests_sha, "assetSha256": packages, "traces": traces,
    "scope": "Actual original Cycle LayeredBoneBlend and OrientationWarping node with explicit ALS spine adaptation, original local/component converters, real sequence/provider at previous real Sync times and controlled orientation/component/alpha inputs. Original node UpdateInternal and persistent history. No Stride, complete Main exposed-handler traversal or production execution."})
unreal.log("LYRA_ORIENTATION_NATIVE_OK traces=9 frames=3528 logical=81 sequences=42 assets_saved=0")

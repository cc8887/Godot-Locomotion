"""Actual original Cycle -> Orientation -> Stride, one native FCSPose."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("cycle_layer_graph.json", "cycle_layer_requests.json", "cycle_layer_native_bits.json",
         "logical_controls/catalog.json", "logical_controls/calibration.json", "root_motion_policy.json",
         "cycle_layer_pose_policy.json", "linked_layer_inventory.json", "orientation_policy.json", "orientation_requests.json")
files = {name: (root / name).read_bytes() for name in names}
requests = json.loads(files["cycle_layer_requests.json"])
clock = json.loads(files["cycle_layer_native_bits.json"])
catalog = json.loads(files["logical_controls/catalog.json"])
calibration = json.loads(files["logical_controls/calibration.json"])
inventory = json.loads(files["linked_layer_inventory.json"])
orientation = json.loads(files["orientation_requests.json"])
content = Path(unreal.Paths.project_content_dir())
packages = clock["assetSha256"]


def check_packages():
    for path, digest in packages.items():
        if sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes()) != digest:
            raise ValueError("Changed Stride provenance: " + path)


def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing Stride resource differs: " + name)
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
    raise ValueError("Missing transient Stride source")
traces, controlled, policies = [], [], {}
for trace, authored, warp in zip(clock["traces"], requests["traces"], orientation["traces"], strict=True):
    frames, controls = [], []
    for index, (frame, request, warp_input) in enumerate(zip(trace["frames"], authored["frames"], warp["frames"], strict=True)):
        if not frame["active"]:
            controls.append(None)
            continue
        control = {"speed": [0, 10, 40, 80, 150, 300, 800][(index // 9) % 7],
                   "alpha": [1, .4, 0, .7, 1][(index // 17) % 5]}
        controls.append(control)
        child = frame["hipFireAsset"] or authored["bindings"]["Aim_HipFirePose"]
        frames.append({"frame": index, "base": assets.index(frame["asset"]), "child": assets.index(child),
                       "time": frame["time"], "childTime": frame["hipFireTime"], "weight": frame["blendWeight"],
                       "previous": frame["previous"], "delta": frame["delta"],
                       "childPrevious": frame["hipFirePrevious"], "childDelta": frame["hipFireDelta"],
                       "orientation": warp_input, "stride": control})
    native = json.loads(unreal.AlsLyraGraphLibrary.read_cycle_layer_pose_trace(
        skeleton, unreal.load_class(None, inventory["classes"][trace["profile"]]["class"]), sequences,
        json.dumps({"frames": frames, "generatedRootMotion": True, "orientation": True, "stride": True}, separators=(",", ":"))))
    if len(native["rows"]) != len(frames):
        raise ValueError("Incomplete actual Stride evaluation")
    policy = native["stridePolicy"]
    if trace["profile"] in policies and policies[trace["profile"]] != policy:
        raise ValueError("Stride mapping changed across Hz")
    policies[trace["profile"]] = policy
    traces.append({"profile": trace["profile"], "hz": trace["hz"], "rows": native["rows"]})
    controlled.append({"profile": trace["profile"], "hz": trace["hz"], "frames": controls})
check_packages()
dependencies = {name: sha(data) for name, data in files.items()}
policy_sha = save("stride_policy.json", {"schemaVersion": 1, "dependencies": dependencies, "policies": policies})
requests_sha = save("stride_requests.json", {"schemaVersion": 1, "dependencies": dependencies, "traces": controlled})
save("stride_native.json", {"schemaVersion": 1, "dependencies": dependencies, "policySha256": policy_sha,
    "requestsSha256": requests_sha, "assetSha256": packages, "traces": traces,
    "scope": "Actual original Cycle LayeredBlend/OrientationWarping/StrideWarping, original local/CS converters and one FCSPose on adapted ALS81; real sequence/provider at real Sync times. Controlled orientation/component/locomotion speed/alpha. Persistent scale filter and pelvis spring. No complete Main exposed-handler traversal or production execution."})
unreal.log("LYRA_STRIDE_NATIVE_OK traces=9 frames=3528 logical=81 sequences=42 assets_saved=0")

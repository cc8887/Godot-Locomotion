"""Original registered linked Cycle root Update/Sync/Evaluate on transient ALS81."""
import copy
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("cycle_layer_graph.json", "cycle_layer_requests.json", "cycle_layer_native_bits.json",
         "logical_controls/catalog.json", "logical_controls/calibration.json", "linked_layer_inventory.json",
         "source_nodes.json", "cycle_source_definitions.json", "cycle_layer_pose_policy.json", "root_motion_policy.json",
         "orientation_policy.json", "stride_policy.json")
files = {name: (root / name).read_bytes() for name in names}
clock = json.loads(files["cycle_layer_native_bits.json"])
catalog = json.loads(files["logical_controls/catalog.json"])
calibration = json.loads(files["logical_controls/calibration.json"])
nodes = json.loads(files["source_nodes.json"])
packages = clock["assetSha256"]
content = Path(unreal.Paths.project_content_dir())


def check_packages():
    for path, digest in packages.items():
        if sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes()) != digest:
            raise ValueError("Changed Cycle runtime provenance: " + path)


def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing Cycle runtime resource differs: " + name)
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
    raise ValueError("Missing transient runtime source")
requests = copy.deepcopy(json.loads(files["cycle_layer_requests.json"]))
requests["sequencePaths"] = assets
angles = [0, 35.123456789, 88.9, -45.001234, 110, -170, 179.999999, -179.999999, 15, -80, 360, 720]
for trace in requests["traces"]:
    for index, frame in enumerate(trace["frames"]):
        # Different values deliberately distinguish Cycle's binding from Start/Pivot.
        frame["main"]["LocalVelocityDirectionAngle"] = angles[(index // 7) % len(angles)]
        frame["main"]["LocalVelocityDirectionAngleWithOffset"] = frame["main"]["LocalVelocityDirectionAngle"] + 123.56789
        frame["main"]["DisplacementSpeed"] += .0123456789
        yaw = [0, 45, -90, 170][(index // 23) % 4]
        half = math.radians(yaw) * .5
        frame["relativeRotation"] = [0, 0, math.sin(half), math.cos(half)]
request_sha = save("cycle_runtime_requests.json", requests)
main = unreal.load_class(None, nodes["classes"]["main"]["class"])
mesh = unreal.load_asset(basis["sourceMesh"])
text = unreal.AlsLyraGraphLibrary.read_cycle_runtime_pose_trace(main, mesh, skeleton, sequences,
    json.dumps(requests, separators=(",", ":")))
if not text:
    raise ValueError("Empty original runtime Cycle trace")
native = json.loads(text)
if len(native["traces"]) != 9 or sum("output" in row for trace in native["traces"] for row in trace["frames"]) != 3528:
    raise ValueError("Incomplete original runtime Cycle trace")
check_packages()
types = {trace["profile"]: trace["propertyTypes"] for trace in native["traces"]}
for trace, authored in zip(native["traces"], requests["traces"], strict=True):
    if trace["propertyTypes"] != types[trace["profile"]] or any(value != "double" for value in trace["propertyTypes"].values()):
        raise ValueError("Changed Cycle binding property types")
    for row, frame in zip(trace["frames"], authored["frames"], strict=True):
        if not frame["active"]:
            continue
        single = lambda value: struct.unpack("f", struct.pack("f", value))[0]
        if row["orientationAngle"] != single(frame["main"]["LocalVelocityDirectionAngle"]) or row["strideSpeed"] != single(frame["main"]["DisplacementSpeed"]) or row["strideNodeAlpha"] != single(row["strideAlpha"]):
            raise ValueError("Original bound Warp value differs from its source")
        if row["orientationAlpha"] != 1 or row["orientationDirection"] != [0, 0, 0]:
            raise ValueError("Changed original Orientation constant inputs")
save("cycle_runtime_bindings.json", {"schemaVersion": 1, "dependencies": {name: sha(data) for name, data in files.items()},
    "profiles": types, "orientationAlpha": 1, "orientationDirection": [0, 0, 0],
    "bindings": {"orientationAngle": ["GetMainAnimBPThreadSafe", "LocalVelocityDirectionAngle"],
                 "strideSpeed": ["GetMainAnimBPThreadSafe", "DisplacementSpeed"], "strideAlpha": "StrideWarpingCycleAlpha"},
    "scope": "Frozen original Cycle bindings from source graph, actual compiled property types and all active original-root exposed-handler readbacks; double to float at each native Warp pin. Three exported providers."})
save("cycle_runtime_native.json", {"schemaVersion": 1, "requestSha256": request_sha,
    "contractSha256": sha(files["cycle_layer_graph.json"]), "assets": clock["assets"],
    "dependencies": {name: sha(data) for name, data in files.items()}, "assetSha256": packages,
    "traces": native["traces"],
    "scope": "Actual registered original Main/ItemAnimLayers, original compiled eight-node Cycle root Initialize/CacheBones/Update, exposed handlers and source callbacks; one actual common Sync; original root Evaluate including source/provider, local HipFire and dual warps. Instance-only transient ALS81 source/mask/spine adaptation and evaluation proxy. Controlled Main snapshot and component transform, not complete Main update, Notify consumers or production Godot host."})
unreal.log("LYRA_CYCLE_RUNTIME_NATIVE_OK traces=9 frames=3780 poseFrames=3528 logical=81 sequences=42 assets_saved=0")

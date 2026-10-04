"""Original Main Cycle ApplyAdditive -> real Linked Cycle and Lean, one Sync."""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("cycle_layer_graph.json", "cycle_runtime_requests.json", "cycle_layer_native_bits.json",
    "logical_controls/catalog.json", "logical_controls/calibration.json", "linked_layer_inventory.json",
    "source_nodes.json", "cycle_source_definitions.json", "cycle_layer_pose_policy.json", "root_motion_policy.json",
    "orientation_policy.json", "stride_policy.json", "main_update_requests.json", "main_update_policy.json",
    "main_lean/catalog.json", "main_lean/inventory.json", "main_lean/behavior.json",
    "main_lean/runtime_policies.json", "main_lean/composition_v3_policy.json")
files = {name: (root / name).read_bytes() for name in names}
clock = json.loads(files["cycle_layer_native_bits.json"])
catalog = json.loads(files["logical_controls/catalog.json"])
calibration = json.loads(files["logical_controls/calibration.json"])
nodes = json.loads(files["source_nodes.json"])
packages = json.loads(files["main_lean/catalog.json"])["assetSha256"]
content = Path(unreal.Paths.project_content_dir())
def protect():
    for path, value in packages.items():
        if sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes()) != value:
            raise ValueError("Changed observed Cycle package: " + path)
def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing immutable observed Cycle fixture differs: " + name)
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
protect()
basis = calibration["calibration"]
rotation = unreal.Quat(*basis["handBasis"]["rotation"])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis["sourceMesh"]).get_editor_property("skeleton"),
    unreal.load_asset(basis["targetMesh"]).get_editor_property("skeleton"), rotation)
assets = [asset["path"] for asset in clock["assets"]]
entries = {entry["target"]: entry for entry in catalog["entries"]}
sequences = [unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
    unreal.load_asset(entries[path]["source"]), unreal.load_asset(path), skeleton, rotation, None) for path in assets]
if any(sequence is None for sequence in sequences):
    raise ValueError("Missing transient Cycle sequence")
requests = copy.deepcopy(json.loads(files["cycle_runtime_requests.json"]))
requests["observeMain"] = True
requests["completeMain"] = True
observations = {trace["hz"]: trace["frames"] for trace in json.loads(files["main_update_requests.json"])["traces"]}
for trace in requests["traces"]:
    for index, frame in enumerate(trace["frames"]):
        frame.pop("main")
        frame["observation"] = copy.deepcopy(observations[trace["hz"]][index])
        frame["delta"] = frame["observation"]["delta"]
        frame["observation"].pop("snapshot")
main = unreal.load_class(None, nodes["classes"]["main"]["class"])
lean_catalog = json.loads(files["main_lean/catalog.json"])
lean_sequences = []
for entry in lean_catalog["entries"]:
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(entry["source"]), unreal.load_asset(entry["target"]), skeleton, rotation,
        lean_sequences[0] if lean_sequences else None)
    if sequence is None:
        raise ValueError("Missing transient Main Lean sequence")
    lean_sequences.append(sequence)
lean_space = unreal.load_asset(json.loads(files["main_lean/inventory.json"])["source"])
text = unreal.AlsLyraGraphLibrary.read_main_cycle_lean_trace(main, unreal.load_asset(basis["sourceMesh"]),
    skeleton, sequences, lean_space, lean_sequences, json.dumps(requests, separators=(",", ":")))
if not text:
    raise ValueError("Empty observed Cycle trace")
native = json.loads(text)
if len(native["traces"]) != 9 or sum("output" in row for trace in native["traces"] for row in trace["frames"]) != 3528:
    raise ValueError("Incomplete observed Cycle trace")
for trace, authored in zip(native["traces"], requests["traces"], strict=True):
    for row, frame in zip(trace["frames"], authored["frames"], strict=True):
        frame["observation"]["snapshot"] = row["observation"]["input"]
        frame["componentInput"] = row["observation"]["componentInput"]
        if row["observation"]["after"]["IsFirstUpdate"] or row["observation"]["tailAfter"]["mode"] != 0:
            raise ValueError("Incomplete full Main macro")
protect()
request_sha = save("main_cycle_lean_requests.json", requests)
payload = {"schemaVersion": 1, "requestSha256": request_sha, "contractSha256": sha(files["cycle_layer_graph.json"]),
    "dependencies": {name: sha(value) for name, value in files.items()}, "assetSha256": packages,
    "assets": clock["assets"], "traces": native["traces"],
    "scope": "Original Main ApplyAdditive17/Linked15/Lean16, no explicit base pose; real Character PropertyAccess/complete original BlueprintThreadSafeUpdateAnimation -> original registered three-provider Cycle callback, common Sync and entire Cycle root including both Warps plus original Main Lean in the same Sync, pose/curve/attributes/root. Explicit previous-graph RootYaw mode, tags, enabled, montage activity and relevance/weight/HipFire inputs; not full Main animation root, Notify/Montage advancement or production."}
save("main_cycle_lean_native.json", payload)
unreal.log("LYRA_MAIN_CYCLE_LEAN_NATIVE_OK traces=9 frames=3780 poseFrames=3528 functions=10 packages=492 assets_saved=0")

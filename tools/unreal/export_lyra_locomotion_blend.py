"""Real main machine weighting/inertia with controlled ALS logical state inputs."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
request_path = Path(os.environ["LYRA_BLEND_REQUESTS"])
if not root.is_absolute() or not request_path.is_absolute():
    raise ValueError("Absolute input/output paths required")
sha = lambda value: hashlib.sha256(value).hexdigest()
request_bytes = request_path.read_bytes()
requests = json.loads(request_bytes)
graph_bytes = (root / "runtime_graph.json").read_bytes()
calibration_bytes = (root / "logical_controls/calibration.json").read_bytes()
catalog_bytes = (root / "logical_controls/catalog.json").read_bytes()
graph = json.loads(graph_bytes)
calibration = json.loads(calibration_bytes)
if (requests["schemaVersion"] != 1 or requests["runtimeGraphSha256"] != sha(graph_bytes) or
        requests["calibrationSha256"] != sha(calibration_bytes) or requests["catalogSha256"] != sha(catalog_bytes) or
        [t["hz"] for t in requests["traces"]] != [30, 60, 120] or
        sum(len(t["frames"]) for t in requests["traces"]) != 840):
    raise ValueError("Stale or incomplete blend inputs")
content = Path(unreal.Paths.project_content_dir())

def package_hash(path):
    return sha((content / (path.split(".", 1)[0].removeprefix("/Game/") + ".uasset")).read_bytes())

assets = calibration["assetSha256"].copy()
for key, row in graph["classes"].items():
    path = row["class"]
    if package_hash(path) != graph["assetSha256"][key]:
        raise ValueError("Changed compiled graph package: " + path)
    assets[path] = package_hash(path)
for path, digest in assets.items():
    if package_hash(path) != digest:
        raise ValueError("Changed source package: " + path)
basis = calibration["calibration"]
source = unreal.load_asset(basis["sourceMesh"])
target = unreal.load_asset(basis["targetMesh"])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    source.get_editor_property("skeleton"), target.get_editor_property("skeleton"),
    unreal.Quat(*basis["handBasis"]["rotation"]))
layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
if layout["logicalBoneNames"] != calibration["layout"]["logicalBoneNames"]:
    raise ValueError("Wrong logical skeleton")
native = json.loads(unreal.AlsLyraGraphLibrary.read_locomotion_blend_trace(
    unreal.load_class(None, graph["classes"]["main"]["class"]), source, skeleton,
    json.dumps(requests, separators=(",", ":"))))
if native["class"] != graph["classes"]["main"]["class"] or len(native["traces"]) != 3:
    raise ValueError("Wrong native blend trace")
for path, digest in assets.items():
    if package_hash(path) != digest:
        raise ValueError("Native probe changed source package: " + path)
payload = {"schemaVersion": 1, "requestSha256": sha(request_bytes), "runtimeGraphSha256": sha(graph_bytes),
           "calibrationSha256": sha(calibration_bytes), "catalogSha256": sha(catalog_bytes),
           "assetSha256": assets, "scope": "Actual compiled main machine TransitionToState/Update/Evaluate and final native Inertialization. "
           "Controlled selected terminal edges and two sampled raw ALS logical poses per state; sparse synthetic curves; no original linked pose graph, "
           "attributes, source clocks, Sync, node callbacks or gameplay host.", "traces": native["traces"]}
output = root / "locomotion_blend_native.json"
if output.exists():
    if json.loads(output.read_bytes()) != payload:
        raise ValueError("Existing native blend trace differs")
else:
    output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_LOCOMOTION_BLEND_NATIVE_OK hz=30,60,120 frames=840 bones=81 assets_saved=0")

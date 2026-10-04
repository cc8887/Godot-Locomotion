"""Original Start callbacks on a real linked instance, followed by common native Sync."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
files = {name: (root / name).read_bytes() for name in (
    "source_nodes.json", "linked_layer_inventory.json", "logical_controls/catalog.json", "logical_controls/calibration.json")}
nodes, inventory, catalog, calibration = map(json.loads, files.values())
assets = dict(calibration["assetSha256"])
assets.update(nodes["assetSha256"])
content = Path(unreal.Paths.project_content_dir())

def package_hash(path):
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())

def save(path, value):
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing Start fixture differs: " + str(path))
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())

if any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Changed Start provenance")
targets = {entry["source"]: entry["target"] for entry in catalog["entries"]}
traces, definitions = [], {}
for profile in ("unarmed", "pistol", "rifle"):
    owner = nodes["classes"][profile]
    sources = [s for s in owner["sources"] if s["functions"]["update"] == "UpdateStartAnim"]
    if len(sources) != 1: raise ValueError("Ambiguous Start node")
    bindings = {group: {direction: targets[path] for direction, path in inventory["classes"][profile]["cardinals"][group].items()}
                for group in ("Jog_Start_Cardinals", "ADS_Start_Cardinals", "Crouch_Start_Cardinals")}
    for group in bindings.values():
        for path in group.values():
            if path not in definitions:
                animation = unreal.load_asset(path)
                curve = unreal.AlsLyraGraphLibrary.read_distance_sequence_data(animation, "Distance")
                if not curve: raise ValueError("Missing exact Distance data: " + path)
                definition = json.loads(curve)
                sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(animation))
                definition["markers"] = sync["markers"]
                definitions[path] = definition
    for hz in (30, 60, 120):
        frames = []
        width = hz // 2
        for frame in range(hz * 6):
            cell, local = divmod(frame, width)
            # First active update selects each of the twelve cardinals. Change
            # all selectors mid-episode to check that Update does not reselect.
            selector = cell if local < width // 2 else (cell + 5) % 12
            frames.append({"delta": 0 if local == 3 else 1e-9 if local == 4 else 1 / hz,
                           "weight": 1e-6 if local == 7 else (1, .73, 1.1)[cell % 3],
                           "active": local != width - 1,
                           "reinitialize": local == 0 or (cell in (2, 6) and local == 5) or (cell == 8 and local == width - 1),
                           "main": {"IsCrouching": selector >= 8, "GameplayTag_IsADS": 4 <= selector < 8,
                                    "LocalVelocityDirection": selector % 4,
                                    "DisplacementSinceLastUpdate": (-1, 0, .0001, .5, 4, 20, 1000)[local % 7]}})
        traces.append({"profile": profile, "class": owner["class"], "nodeIndex": sources[0]["nodeIndex"],
                       "hz": hz, "bindings": bindings, "frames": frames})
requests = {"schemaVersion": 1, "traces": traces}
request_sha = save(root / "start_source_requests.json", requests)
main = unreal.load_class(None, nodes["classes"]["main"]["class"])
mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
text = unreal.AlsLyraGraphLibrary.read_start_source_trace(main, mesh, json.dumps(requests, separators=(",", ":")))
if not text: raise ValueError("Empty native Start result")
native = json.loads(text)
for path, definition in definitions.items():
    settled = json.loads(unreal.AlsLyraGraphLibrary.read_distance_sequence_data(unreal.load_asset(path), "Distance"))
    if settled != {k: v for k, v in definition.items() if k != "markers"}:
        raise ValueError("Distance codec changed during native traversal: " + path)
if len(native["traces"]) != 9 or any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Incomplete Start trace or package changed")
payload = {"schemaVersion": 1, "requestSha256": request_sha,
           "dependencies": {name: sha(value) for name, value in files.items()},
           "assetSha256": assets, "traces": native["traces"],
           "scope": "Real registered Main/Linked instance, instance-only ALS Start bindings, original compiled evaluator callbacks via native pose link, one common native Sync. Controlled Main inputs and one source; no provider root, state machine, Notify/Montage, pose/root output or ordinary Demo acceptance."}
save(root / "start_source_native_bits.json", payload)
policies = {}
for trace in native["traces"]:
    profile = trace["profile"]
    if profile in policies and policies[profile] != trace["policy"]:
        raise ValueError("Start static policy changed during traversal")
    policies[profile] = trace["policy"]
save(root / "start_source_definitions.json", {"schemaVersion": 1, "requestSha256": request_sha,
     "assets": definitions, "policies": policies,
     "scope": "Actual sequence distance data/codec and static instance policy. No expected callback, clock, rate or Sync outputs."})
unreal.log(f"LYRA_START_SOURCE_NATIVE_OK traces=9 frames={sum(len(t['frames']) for t in traces)} packages={len(assets)} assets={len(definitions)} assets_saved=0")

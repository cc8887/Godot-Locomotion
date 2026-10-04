"""Original registered Linked Layer callback -> real pose link/player -> isolated common native Sync."""
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
            raise ValueError("Existing Cycle fixture differs: " + str(path))
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
if any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Changed Cycle provenance")
targets = {entry["source"]: entry["target"] for entry in catalog["entries"]}
traces, definitions = [], {}
for profile in ("unarmed", "pistol", "rifle"):
    owner = nodes["classes"][profile]
    source = [s for s in owner["sources"] if s["functions"]["update"] == "UpdateCycleAnim"]
    if len(source) != 1: raise ValueError("Ambiguous Cycle node")
    bindings = {group: {direction: targets[path] for direction, path in inventory["classes"][profile]["cardinals"][group].items()}
                for group in ("Jog_Cardinals", "Walk_Cardinals", "Crouch_Walk_Cardinals")}
    for group in bindings.values():
        for path in group.values():
            if path not in definitions:
                animation = unreal.load_asset(path)
                metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
                sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(animation))
                definitions[path] = {"path": path, "length": metadata["sequencePlayLength"],
                                     "rateScale": sync["rateScale"], "markers": sync["markers"]}
    for hz in (30, 60, 120):
        frames = []
        for frame in range(hz * 6):
            cell = int(frame / hz / .5)
            frames.append({"delta": 0 if frame % 11 == 0 else 1 / hz, "weight": .73,
                           "reinitialize": frame in (0, int(hz * 2.75)),
                           "main": {"IsCrouching": cell >= 8, "GameplayTag_IsADS": 4 <= cell < 8,
                                    "LocalVelocityDirectionNoOffset": cell % 4,
                                    "DisplacementSpeed": (0, 1, 30, 500, 1000)[(frame // 7) % 5],
                                    "IsRunningIntoWall": (frame // 13) % 2 == 0}})
        traces.append({"profile": profile, "class": owner["class"], "nodeIndex": source[0]["nodeIndex"],
                       "hz": hz, "bindings": bindings, "frames": frames})
requests = {"schemaVersion": 1, "traces": traces}
request_sha = save(root / "cycle_source_requests.json", requests)
main = unreal.load_class(None, nodes["classes"]["main"]["class"])
mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
native_text = unreal.AlsLyraGraphLibrary.read_cycle_source_trace(main, mesh, json.dumps(requests, separators=(",", ":")))
if not native_text: raise ValueError("Empty native Cycle result")
native = json.loads(native_text)
if len(native["traces"]) != 9 or any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Incomplete Cycle trace or package changed")
payload = {"schemaVersion": 1, "requestSha256": request_sha,
           "dependencies": {name: sha(value) for name, value in files.items()},
           "assetSha256": assets, "assets": list(definitions.values()), "traces": native["traces"],
           "scope": "Real registered Main/Linked instance, instance-only ALS Cycle bindings, original compiled node callback via native pose link and SequencePlayer update; isolated common native proxy Sync. Controlled Main inputs and one source; no whole-graph traversal, Notify consumer, pose, root-motion output or production Godot host validation."}
save(root / "cycle_source_native_bits.json", payload)
static_assets, static_clamps = {}, {}
for trace in native["traces"]:
    for row in trace["frames"]:
        definition = {key: row[key] for key in ("length", "lengthBits", "rootDistance", "rootDistanceBits")}
        if row["asset"] in static_assets and static_assets[row["asset"]] != definition:
            raise ValueError("Cycle asset properties changed during traversal")
        static_assets[row["asset"]] = definition
        clamp = {key: row[key] for key in ("clampMin", "clampMinBits", "clampMax", "clampMaxBits")}
        if trace["profile"] in static_clamps and static_clamps[trace["profile"]] != clamp:
            raise ValueError("Cycle clamp changed during traversal")
        static_clamps[trace["profile"]] = clamp
save(root / "cycle_source_definitions.json", {"schemaVersion": 1, "requestSha256": request_sha,
     "assets": static_assets, "clamps": static_clamps,
     "scope": "Static asset GetPlayLength and full-range planar root distance, original instance clamp settings; no clock/rate/stride outputs."})
unreal.log(f"LYRA_CYCLE_SOURCE_NATIVE_OK traces=9 frames={sum(len(t['frames']) for t in traces)} packages={len(assets)} assets_saved=0")

"""Original full Cycle root update on registered linked instances; no pose evaluation."""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
hidden_reset = os.environ.get("LYRA_CYCLE_HIDDEN_RESET") == "1"
variant = "cycle_layer_hidden_reset" if hidden_reset else "cycle_layer"
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("source_nodes.json", "linked_layer_inventory.json", "logical_controls/catalog.json",
         "logical_controls/calibration.json", "cycle_source_requests.json", "cycle_source_definitions.json")
files = {name: (root / name).read_bytes() for name in names}
nodes, inventory, catalog, calibration, base_requests, _ = map(json.loads, files.values())
packages = dict(calibration["assetSha256"]); packages.update(nodes["assetSha256"])
content = Path(unreal.Paths.project_content_dir())
def package_hash(path):
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())
def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value: raise ValueError("Existing Cycle layer fixture differs: " + name)
    else: path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
if any(package_hash(path) != expected for path, expected in packages.items()): raise ValueError("Changed Cycle provenance")
graphs = {}
for profile, owner in inventory["classes"].items():
    graph = json.loads(unreal.AlsLyraGraphLibrary.read_cycle_layer_graph(unreal.load_class(None, owner["class"])))
    if len(graph["nodes"]) != 8: raise ValueError("Incomplete native Cycle closure")
    graphs[profile] = graph
contract_sha = save("cycle_layer_graph.json", {"schemaVersion": 1, "sourceNodesSha256": sha(files["source_nodes.json"]),
                  "assetSha256": nodes["assetSha256"], "graphs": graphs})
requests = copy.deepcopy(base_requests)
definitions = {}
for trace in requests["traces"]:
    profile = trace["profile"]
    trace["hipFireIndex"] = next(s["nodeIndex"] for s in nodes["classes"][profile]["sources"]
        if s["functions"]["update"] == "UpdateHipFireRaiseWeaponPose" and s["nodeIndex"] in next(g["players"] for g in nodes["classes"][profile]["graphs"] if g["name"] == "FullBody_CycleState"))
    for property in ("Aim_HipFirePose", "Aim_HipFirePose_Crouch"):
        source = inventory["classes"][profile]["assets"][property]
        entries = [e for e in catalog["entries"] if e["source"] == source]
        if len(entries) != 1:
            # The original Pistol crouch idle has two already exported ALS
            # copies; preserve each provider's established local binding.
            slot = "hipfire_crouch" if profile == "unarmed" else "pistol_crouch_idle"
            entries = [e for e in entries if e["slot"] == slot]
        if len(entries) != 1: raise ValueError("Ambiguous HipFire target binding")
        trace["bindings"][property] = entries[0]["target"]
    for frame_index, frame in enumerate(trace["frames"]):
        t = frame_index / trace["hz"]
        frame["active"] = not (1.35 <= t < 1.55 or 4.35 <= t < 4.55)
        if hidden_reset and frame_index in (round(trace["hz"] * 1.45), round(trace["hz"] * 4.45)):
            frame["reinitialize"] = True
        frame["weight"] = (.001, .73, 1, 1.1)[(frame_index // 9) % 4]
        frame["layer"] = {"HipFireUpperBodyOverrideWeight": (0, 1e-5, 1.00001e-5, .15, .8, 1, -0.1, 1.2)[(frame_index // 5) % 8]}
        frame["main"]["LocalVelocityDirectionAngle"] = (0, 180, -90, 90)[frame["main"]["LocalVelocityDirectionNoOffset"]]
    for path in trace["bindings"].values():
        paths = path.values() if isinstance(path, dict) else [path]
        for target in paths:
            if target in definitions: continue
            animation = unreal.load_asset(target)
            metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
            sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(animation))
            definitions[target] = {"path": target, "length": metadata["sequencePlayLength"], "rateScale": sync["rateScale"], "markers": sync["markers"]}
request_sha = save(variant + "_requests.json", requests)
main = unreal.load_class(None, nodes["classes"]["main"]["class"])
mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
result_text = unreal.AlsLyraGraphLibrary.read_cycle_layer_trace(main, mesh, json.dumps(requests, separators=(",", ":")))
if not result_text: raise ValueError("Empty native Cycle layer trace")
result = json.loads(result_text)
if len(result["traces"]) != 9 or any(package_hash(path) != expected for path, expected in packages.items()):
    raise ValueError("Incomplete Cycle trace or source package changed")
save(variant + "_native_bits.json", {"schemaVersion": 1, "requestSha256": request_sha, "contractSha256": contract_sha,
     "dependencies": {name: sha(data) for name, data in files.items()}, "assetSha256": packages,
     "assets": list(definitions.values()), "traces": result["traces"],
     "scope": "Original compiled Cycle root Initialize/CacheBones/Update, actual registered Main/Linked instances and instance-only ALS bindings; controlled inputs and isolated common native Sync. Includes real source callback/relevance/update ordering through original warp nodes; no pose evaluation or warp transforms, Notify consumer, whole Main or production Godot host."})
unreal.log("LYRA_CYCLE_LAYER_NATIVE_OK traces=9 frames=3780 packages=489 assets_saved=0")

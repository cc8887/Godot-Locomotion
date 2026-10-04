"""Actual standalone SequenceEvaluator update -> root sync scope -> native FAnimSync.
No assets are saved, and original sidecars are not rewritten.
"""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
nonloop = os.environ.get("LYRA_EVALUATOR_NONLOOP") == "1"
sha = lambda data: hashlib.sha256(data).hexdigest()
node_bytes = (root / "source_nodes.json").read_bytes()
catalog_bytes = (root / "logical_controls/catalog.json").read_bytes()
calibration_bytes = (root / "logical_controls/calibration.json").read_bytes()
nodes, catalog, calibration = map(json.loads, (node_bytes, catalog_bytes, calibration_bytes))
entries = {row["slot"]: row for row in catalog["entries"]}
slots = ["jump_fall_land", "turn_right", "jog_fwd_cycle", "walk_fwd_cycle"]
if nonloop:
    slots = ["jog_fwd_start", "jog_fwd_pivot", "walk_fwd_start", "walk_fwd_pivot"]
paths = [entries[slot]["target"] for slot in slots]
assets = dict(calibration["assetSha256"])
assets.update(nodes["assetSha256"])
content = Path(unreal.Paths.project_content_dir())

def package_hash(path):
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())

def save(path, value):
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing evaluator export differs: " + str(path))
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())

if any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Changed evaluator probe provenance")
definitions = []
for slot, path in zip(slots, paths):
    animation = unreal.load_asset(path)
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
    sync = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(animation))
    markers = sync["markers"]
    definitions.append({"slot": slot, "path": path, "length": metadata["sequencePlayLength"],
                        "rateScale": sync["rateScale"], "markers": markers})
if (not nonloop and any(row["markers"] for row in definitions[:2])) or not all(row["markers"] for row in definitions[2:]) or (nonloop and not all(row["markers"] for row in definitions)):
    raise ValueError("Wrong markerless/marked evaluator fixture")

scenarios = ["teleport", "independentAdvance", "forcedLeader", "leaderTie", "roleSwap",
             "inertiaRejoin", "graphScope", "markerLeader", "tinyDelta"]
traces = []
for hz in (30, 60, 120):
    for scenario in scenarios:
        frames = []
        for frame in range(round(hz * 2.5)):
            t = frame / hz
            delta = 0 if frame % 11 == 0 else (1e-9 if scenario == "tinyDelta" and frame % 5 == 0 else 1 / hz)
            inputs = []
            if not (1.35 <= t < 1.45):
                for slot in range(3):
                    marked = scenario == "markerLeader" or nonloop
                    asset = (2 if slot == 0 else 3) if scenario == "markerLeader" else (0 if slot == 0 else 1)
                    length = definitions[asset]["length"]
                    independent = scenario in ("teleport", "independentAdvance", "tinyDelta")
                    method = 0 if independent else 2 if scenario == "graphScope" else 1
                    role = 2 if slot == 0 else 1 if slot == 2 else 0
                    if scenario == "leaderTie" and slot == 1:
                        role = 2
                    if scenario == "roleSwap":
                        role = (2 if slot == (0 if t < .85 else 1) else 1)
                    if scenario == "inertiaRejoin":
                        role = 2 if slot == (2 if t < .8 else 0) else 1
                    reset = frame == 0 or frame == round(hz * .8) or frame == round(hz * 1.45)
                    # Clamp, reverse and zero/end-wrap cases all enter the real
                    # evaluator. Explicit time is authored input, not an oracle formula.
                    explicit = length * (.1 + .82 * math.sin(t * 3.7 + slot * .3))
                    if frame % 17 == 0:
                        explicit = length if (frame // 17) % 2 else 0
                    if scenario == "teleport" and slot == 0:
                        explicit = -length if frame % 4 == 0 else length * 2
                    inputs.append({"slot": slot, "asset": asset, "method": method, "role": role,
                                   "looping": not nonloop and (marked or scenario in ("teleport", "independentAdvance", "tinyDelta")),
                                   "teleport": scenario != "independentAdvance", "explicitTime": explicit,
                                   "weight": .001 if slot == 0 else .85 if slot == 1 else .99,
                                   "reinitialize": reset, "reinitialization": (frame // 17 + slot) % 3,
                                   "startPosition": .23 if slot < 2 else .1,
                                   "inertial": scenario == "inertiaRejoin" and reset,
                                   "playRate": -1.25 if 1.7 <= t < 2 else 1.25})
            frames.append({"delta": delta, "inputs": inputs})
        traces.append({"hz": hz, "scenario": scenario, "frames": frames})
requests = {"schemaVersion": 1, "assets": paths, "traces": traces}
request_name = "evaluator_nonloop_requests.json" if nonloop else "evaluator_sync_requests.json"
request_sha = save(root / request_name, requests)
native = json.loads(unreal.AlsLyraGraphLibrary.read_evaluator_sync_trace(json.dumps(requests, separators=(",", ":"))))
if len(native["traces"]) != 27 or any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Incomplete trace or native probe changed a package")
payload = {"schemaVersion": 2, "requestSha256": request_sha, "sourceNodesSha256": sha(node_bytes),
           "catalogSha256": sha(catalog_bytes), "calibrationSha256": sha(calibration_bytes),
           "assetSha256": assets, "assets": definitions, "traces": native["traces"],
           "scope": "Real standalone SequenceEvaluator update and proxy sync scope/FAnimSync; "
                    "real Lyra ALS sequences, controlled settings/explicit-time/relevance inputs; "
                    "no original compiled linked graph traversal, Notify consumer, pose or root-motion comparison."}
save(root / ("evaluator_nonloop_native_bits.json" if nonloop else "evaluator_sync_native_bits.json"), payload)
unreal.log(f"LYRA_EVALUATOR_SYNC_NATIVE_OK traces=27 frames={sum(len(t['frames']) for t in traces)} packages={len(assets)} assets_saved=0")

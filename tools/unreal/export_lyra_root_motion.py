"""Actual unlocked root intervals/provider attributes and original Cycle blend.

Transient ALS81 sequences only; never saves assets or modifies old fixtures.
"""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
single = lambda value: struct.unpack("f", struct.pack("f", value))[0]
names = ("cycle_layer_graph.json", "cycle_layer_requests.json", "cycle_layer_native_bits.json",
         "logical_controls/catalog.json", "logical_controls/calibration.json",
         "logical_controls/curve_bank.json", "cycle_layer_pose_policy.json", "linked_layer_inventory.json")
files = {name: (root / name).read_bytes() for name in names}
graph, requests, clock, catalog, calibration, curves, policy, inventory = map(json.loads, files.values())
content = Path(unreal.Paths.project_content_dir())
packages = clock["assetSha256"]
def check_packages():
    for path, digest in packages.items():
        file = content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
        if sha(file.read_bytes()) != digest:
            raise ValueError("Changed RootMotion provenance: " + path)
def save(name, value):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing RootMotion resource differs: " + name)
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
check_packages()
basis = calibration["calibration"]
rotation = unreal.Quat(*basis["handBasis"]["rotation"])
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis["sourceMesh"]).get_editor_property("skeleton"),
    unreal.load_asset(basis["targetMesh"]).get_editor_property("skeleton"), rotation)
entries = [e for e in catalog["entries"] if json.loads((root / "logical_controls" / e["file"]).read_bytes())["metadata"]["additiveType"] == "AAT_None"]
if len(entries) != 189:
    raise ValueError("Incomplete ordinary root source inventory")
sequences = []
ranges = []
for index, entry in enumerate(entries):
    sequence = unreal.AlsLyraControlRigLibrary.create_weapon_sequence(
        unreal.load_asset(entry["source"]), unreal.load_asset(entry["target"]), skeleton, rotation, None)
    if sequence is None:
        raise ValueError("Missing transient root sequence")
    sequences.append(sequence)
    length = entry["playLength"]
    cases = [(0, 0, True, 0), (.37, 0, True, .37),
             (0, .03, False, .016666667), (.42, .033333334, True, .420000017),
             (.9, .3, True, .9), (.12, -.4, True, .12),
             (.35, 2.4, True, .35), (.8, -2.3, True, .8),
             (.9, .4, False, .9), (.1, -.3, False, .1),
             (1, .1, True, 1), (0, -.1, True, 0),
             (0, 1, True, 1.1), (1, -1, True, -.1),
             (.5, .000001, True, .5), (.5, -.000001, True, .50000004)]
    for case, (previous, delta, looping, sample) in enumerate(cases):
        ranges.append({"sequence": index, "case": case, "previous": single(previous * length),
                       "delta": single(delta * length), "looping": looping, "sampleTime": sample * length})
native_ranges = json.loads(unreal.AlsLyraGraphLibrary.read_root_motion_trace(
    sequences, json.dumps({"ranges": ranges}, separators=(",", ":"))))
if len(native_ranges["rows"]) != len(ranges):
    raise ValueError("Incomplete native root interval rows")
indices = {e["target"]: index for index, e in enumerate(entries)}
traces, probes = [], []
for trace, authored in zip(clock["traces"], requests["traces"], strict=True):
    frames = []
    for index, frame in enumerate(trace["frames"]):
        if not frame["active"]:
            continue
        child = frame["hipFireAsset"] or authored["bindings"]["Aim_HipFirePose"]
        frames.append({"frame": index, "base": indices[frame["asset"]], "child": indices[child],
                       "time": frame["time"], "childTime": frame["hipFireTime"], "weight": frame["blendWeight"],
                       "previous": frame["previous"], "delta": frame["delta"],
                       "childPrevious": frame["hipFirePrevious"], "childDelta": frame["hipFireDelta"]})
    native = json.loads(unreal.AlsLyraGraphLibrary.read_cycle_layer_pose_trace(
        skeleton, unreal.load_class(None, inventory["classes"][trace["profile"]]["class"]), sequences,
        json.dumps({"frames": frames, "includeProbes": not traces, "generatedRootMotion": True}, separators=(",", ":"))))
    if not native["generatedRootMotion"] or len(native["rows"]) != len(frames):
        raise ValueError("Incomplete provider-enabled Cycle pose")
    if "probes" in native:
        probes = native["probes"]
    traces.append({"profile": trace["profile"], "hz": trace["hz"], "rows": native["rows"]})
check_packages()
root_policy_sha = save("root_motion_policy.json", {"schemaVersion": 1,
    "dependencies": {name: sha(files[name]) for name in ("logical_controls/catalog.json", "logical_controls/calibration.json", "cycle_layer_pose_policy.json")},
    **{key: native_ranges[key] for key in ("name", "type", "namespace", "blend")}, "bone": "root"})
save("root_motion_native.json", {"schemaVersion": 1, "dependencies": {name: sha(data) for name, data in files.items()},
    "rootPolicySha256": root_policy_sha,
    "assetSha256": packages, "slots": [e["slot"] for e in entries], "requests": ranges,
    "root": native_ranges, "traces": traces, "probes": probes,
    "scope": "Actual UAnimSequence raw root track, ExtractRootMotion and registered provider intervals on 189 transient ALS81 nonadditive sequences; original Cycle LayeredBoneBlend Evaluate at prior real Sync results with generated root attributes. No warps, whole Main or production execution."})
unreal.log("LYRA_ROOT_MOTION_NATIVE_OK sequences=189 ranges=3024 traces=9 frames=3528 probes=72 assets_saved=0")

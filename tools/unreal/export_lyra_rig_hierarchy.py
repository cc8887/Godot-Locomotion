"""Read actual FootPlant hierarchy writes, offset constraints and lazy reads."""
import hashlib
import itertools
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
repo = Path(__file__).resolve().parents[2]
prefix = "rig_hierarchy_v1"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())
graph = load("footplant_rig_graph_v1.json")
previous = {str(p.relative_to(root)).replace("\\", "/"): sha(p)
            for p in root.rglob("*.json") if not p.name.startswith(prefix)}
if (root / (prefix + "_native.json")).exists():
    previous = load(prefix + "_native.json")["previousFixtureSha256"]
packages = graph["assetSha256"]
content = Path(unreal.Paths.project_content_dir())


def protect():
    for p, h in previous.items():
        assert sha(root / p) == h, p
    for p, h in packages.items():
        assert sha(content / (p.split(".")[0].removeprefix("/Game/") + ".uasset")) == h, p


def save(kind, value):
    p = root / (prefix + "_" + kind + ".json")
    if p.exists():
        assert json.loads(p.read_bytes()) == value, "Immutable hierarchy capture changed: " + kind
    else:
        p.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False), encoding="utf-8")
    return sha(p)


def transform(n, scale=None):
    a = n * .037
    axis = [math.sin(n * .11), math.cos(n * .13), .4]
    length = math.sqrt(sum(v*v for v in axis))
    return dict(p=[math.sin(n * .31)*15, math.cos(n * .23)*12, n % 7 * 3],
                q=[v/length*math.sin(a/2) for v in axis] + [math.cos(a/2)],
                s=[1, 1, 1] if scale is None else scale)


def operation(action, name, pose, initial, local, children=True, force=False):
    return dict(action=action, name=name, control=name.endswith("Ctrl"),
                transform=pose, initial=initial, local=local, children=children, force=force)


protect()
requests = dict(schemaVersion=1, traces=[])
names = ["RootCtrl", "OffsetCtrl", "BodyCtrl", "PelvisCtrl", "ChestCtrl",
         "LeftKneePVCtrl", "RightKneePVCtrl", "root", "pelvis", "thigh_l",
         "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r", "ik_foot_root"]
for initial, local, children in itertools.product([False, True], repeat=3):
    batches = []
    for cycle in range(4):
        ops = []
        for j, name in enumerate(names):
            t = transform(17*cycle+j+1)
            ops.append(operation("pose", name, t, initial, local, children))
            if name.endswith("Ctrl"):
                ops.append(operation("offset", name, transform(29*cycle+j+3),
                                     initial, not local, children))
            if j % 3 == 0:
                ops.append(dict(action="read", name=name, control=name.endswith("Ctrl"),
                                offset=False, initial=initial, local=not local))
        batches.append(dict(operations=ops))
    requests["traces"].append(dict(name=f"initial{int(initial)}-local{int(local)}-children{int(children)}",
                                  batches=batches))
ops = []
for name in ("pelvis", "BodyCtrl", "PelvisCtrl"):
    t = transform(71)
    near = dict(t, p=[t["p"][0]+.00001, *t["p"][1:]])
    far = dict(t, p=[t["p"][0]+.001, *t["p"][1:]])
    for action in ("pose", "offset") if name.endswith("Ctrl") else ("pose",):
        for pose, force in ((t, False), (near, False), (near, True), (far, False)):
            ops.append(operation(action, name, pose, False, True, True, force))
            ops.append(dict(action="read", name=name, control=name.endswith("Ctrl"),
                            offset=action == "offset", initial=False, local=True))
requests["traces"].append(dict(name="tolerance-and-force", batches=[dict(operations=ops)]))
batches = []
for scale in ([1.3, .8, 1.1], [-1, .8, 1.2], [0, 1, 1], [0, 0, 0]):
    ops = [dict(action="reset")]
    for j, name in enumerate(("RootCtrl", "OffsetCtrl", "BodyCtrl", "PelvisCtrl", "root", "pelvis", "thigh_l", "calf_l", "foot_l")):
        ops.append(operation("pose", name, transform(j+9, scale), False, True, True))
        ops.append(dict(action="read", name=name, control=name.endswith("Ctrl"), offset=False, initial=False, local=False))
    ops.append(operation("pose", "calf_l", transform(47, scale), False, False, False))
    batches.append(dict(operations=ops))
requests["traces"].append(dict(name="scale-reset", batches=batches))

text = unreal.AlsLyraRigHierarchyLibrary.read_hierarchy(
    unreal.load_class(None, graph["rig"] + "_C"), json.dumps(requests, separators=(",", ":")))
assert text
native = json.loads(text)
(repo / "artifacts/lyra-analysis/rig-hierarchy-diagnostic.json").write_text(
    json.dumps(native, separators=(",", ":"), allow_nan=False), encoding="utf-8")
assert len(native["traces"]) == len(requests["traces"]) == 10
batches = reads = writes = resets = 0
for q, t in zip(requests["traces"], native["traces"], strict=True):
    assert q["name"] == t["name"] and len(q["batches"]) == len(t["batches"])
    assert len(t["initial"]) == len(t["layout"]) == 98
    for name, row in t["layout"].items():
        assert len(row["parents"]) <= 1, name
        if row["type"] == "Control":
            assert not row["limitsEnabled"] and not row["animationChannel"], (name, row)
            assert all(w == dict(current=[1, 1, 1], initial=[1, 1, 1]) for w in row["weights"])
    for qb, row in zip(q["batches"], t["batches"], strict=True):
        batches += 1
        assert len(row["hierarchy"]) == 98
        actions = [v["action"] for v in qb["operations"]]
        reads += actions.count("read")
        writes += actions.count("pose") + actions.count("offset")
        resets += actions.count("reset")
        assert len(row["reads"]) == actions.count("read")
source = repo / "tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter"
source_sha = {p: sha(source / p) for p in ("Private/AlsLyraRigHierarchyLibrary.cpp", "Public/AlsLyraRigHierarchyLibrary.h")}
for tree in ("source", "package"):
    for p, h in source_sha.items():
        assert sha(repo / "artifacts/unreal/gasp58-lyra-masks" / tree / "AlsV4AssetExporter/Source/AlsV4AssetExporter" / p) == h
protect()
native.update(schemaVersion=1, stage="OriginalFootPlantHierarchyWrites",
              requestSha256=save("requests", requests), assetSha256=packages,
              previousFixtureSha256=previous, probeSourceSha256=source_sha,
              dependencies={"footplant_rig_graph_v1.json": sha(root / "footplant_rig_graph_v1.json"),
                            "footplant_rig_ground_v2_program.json": sha(root / "footplant_rig_ground_v2_program.json")},
              counts=dict(traces=10, batches=batches, reads=reads, writes=writes, resets=resets))
save("native", native)
protect()
unreal.log("LYRA_RIG_HIERARCHY_NATIVE_OK traces=10 batches=%d reads=%d writes=%d resets=%d assets_saved=0" %
           (batches, reads, writes, resets))

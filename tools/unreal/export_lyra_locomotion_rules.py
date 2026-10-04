"""Actual compiled transition predicates; source times/Sync selection are separate."""
import hashlib
import json
import os
import random
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute output root required")
graph_bytes = (root / "runtime_graph.json").read_bytes()
graph = json.loads(graph_bytes)
main = graph["classes"]["main"]
calibration = json.loads((root / "logical_controls/calibration.json").read_bytes())
mesh_path = calibration["calibration"]["sourceMesh"]
content = Path(unreal.Paths.project_content_dir())
package = content / (main["class"].split(".")[0].removeprefix("/Game/") + ".uasset")
sha = lambda value: hashlib.sha256(value).hexdigest()
before = sha(package.read_bytes())
if before != graph["assetSha256"]["main"]:
    raise ValueError("Compiled graph changed")
boolean_names = ["HasAcceleration", "HasVelocity", "GameplayTag_IsMelee", "IsRunningIntoWall", "LinkedLayerChanged",
                 "CrouchStateChange", "ADSStateChanged", "IsJumping", "IsFalling", "IsOnGround"]
rng = random.Random(20260930)
rows = []
for state in range(12):
    for case in range(48):
        fields = {name: bool(rng.getrandbits(1)) for name in boolean_names}
        fields.update({"LocalVelocity2D": [rng.choice([-100, 0, 100]), rng.choice([-100, 0, 100]), 0],
                       "LocalAcceleration2D": [rng.choice([-100, 0, 100]), rng.choice([-100, 0, 100]), 0],
                       "StartDirection": rng.randrange(4), "LocalVelocityDirection": rng.randrange(4),
                       "PivotInitialDirection": rng.randrange(4), "DisplacementSpeed": rng.choice([0, 9.999, 10, 10.001, 100]),
                       "RootYawOffset": rng.choice([-60.001, -60, 0, 60, 60.001]),
                       "LastPivotTime": rng.choice([-0.001, 0, 0.001]),
                       "TimeToJumpApex": rng.choice([0, 0.399999, 0.4, 0.400001]),
                       "GroundDistance": rng.choice([0, 199.999, 200, 200.001])})
        rows.append({"state": state, "elapsed": rng.choice([0, 0.149999, 0.15, 0.150001, 1]), "fields": fields})
requests = {"rows": rows}
native = json.loads(unreal.AlsLyraGraphLibrary.read_locomotion_rule_probe(unreal.load_class(None, main["class"]),
    unreal.load_asset(mesh_path), json.dumps(requests, separators=(",", ":"))))
if len(native["rows"]) != len(rows) or sha(package.read_bytes()) != before:
    raise ValueError("Incomplete probe or changed package")
payload = {"schemaVersion": 1, "runtimeGraphSha256": sha(graph_bytes), "mainAssetSha256": before,
           "mesh": mesh_path, "scope": "Compiled non-automatic rule handlers; no active notify states. Sync validity and automatic source remaining-time selection are separate.",
           "rows": [{**request, "rules": result["rules"]} for request, result in zip(rows, native["rows"])]}
path = root / "locomotion_rules_native.json"
if path.exists():
    if json.loads(path.read_bytes()) != payload:
        raise ValueError("Existing compiled rule probe differs")
else:
    path.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_LOCOMOTION_RULES_NATIVE_OK cases=" + str(len(rows)) + " rules=" + str(sum(len(r["rules"]) for r in native["rows"])) + " assets_saved=0")

"""Actual FindValidTransition against the established native-rule requests."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute output root required")
graph_bytes = (root / "runtime_graph.json").read_bytes()
rule_bytes = (root / "locomotion_rules_native.json").read_bytes()
graph = json.loads(graph_bytes)
rules = json.loads(rule_bytes)
sha = lambda value: hashlib.sha256(value).hexdigest()
main = graph["classes"]["main"]
content = Path(unreal.Paths.project_content_dir())
package = content / (main["class"].split(".")[0].removeprefix("/Game/") + ".uasset")
if rules["runtimeGraphSha256"] != sha(graph_bytes) or sha(package.read_bytes()) != rules["mainAssetSha256"]:
    raise ValueError("Stale native selection inputs")
rows = [{k: row[k] for k in ["state", "elapsed", "fields"]} for row in rules["rows"] if row["state"] not in [5, 9]]
native = json.loads(unreal.AlsLyraGraphLibrary.read_locomotion_rule_probe(unreal.load_class(None, main["class"]),
    unreal.load_asset(rules["mesh"]), json.dumps({"rows": rows}, separators=(",", ":"))))
if len(native["rows"]) != 480 or sha(package.read_bytes()) != rules["mainAssetSha256"]:
    raise ValueError("Incomplete native selection or changed package")
payload = {"schemaVersion": 1, "runtimeGraphSha256": sha(graph_bytes), "ruleProbeSha256": sha(rule_bytes),
           "scope": "Actual FindValidTransition in a live initialized main instance, with no active source/Sync/notify state.",
           "rows": [{**request, "selection": result["selection"]} for request, result in zip(rows, native["rows"])]}
path = root / "locomotion_selection_native.json"
if path.exists():
    if json.loads(path.read_bytes()) != payload:
        raise ValueError("Existing selection oracle differs")
else:
    path.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_LOCOMOTION_SELECTION_NATIVE_OK cases=480 assets_saved=0")

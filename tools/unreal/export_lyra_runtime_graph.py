"""Read compiled main/linked state definitions without saving assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute LYRA_OUTPUT_ROOT required")
inventory_bytes = (root / "linked_layer_inventory.json").read_bytes()
inventory = json.loads(inventory_bytes)
paths = {"main": "/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C"}
paths.update({name: row["class"] for name, row in inventory["classes"].items()})
content = Path(unreal.Paths.project_content_dir())
sha = lambda value: hashlib.sha256(value).hexdigest()
def package_bytes(path):
    return (content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes()
hashes = {name: sha(package_bytes(path)) for name, path in paths.items()}
graphs = {}
for name, path in paths.items():
    graph = json.loads(unreal.AlsLyraGraphLibrary.read_runtime_graph(unreal.load_class(None, path)))
    if graph["class"] != path:
        raise ValueError("Wrong compiled class")
    graphs[name] = graph
    unreal.log("LYRA_RUNTIME_GRAPH_CLASS_OK name=" + name + " machines=" + str(len(graph["machines"])))
if any(sha(package_bytes(path)) != hashes[name] for name, path in paths.items()):
    raise ValueError("Compiled graph read changed a package")
payload = {"schemaVersion": 1, "inventorySha256": sha(inventory_bytes), "assetSha256": hashes, "classes": graphs}
path = root / "runtime_graph.json"
if path.exists():
    if json.loads(path.read_bytes()) != payload:
        raise ValueError("Existing runtime graph differs")
else:
    path.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_RUNTIME_GRAPH_OK classes=" + str(len(graphs)) + " assets_saved=0")

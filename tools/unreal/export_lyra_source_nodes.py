"""Read compiled source nodes, callback bindings and layer ownership; never save assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute LYRA_OUTPUT_ROOT required")
sha = lambda data: hashlib.sha256(data).hexdigest()
inventory_bytes = (root / "linked_layer_inventory.json").read_bytes()
runtime_bytes = (root / "runtime_graph.json").read_bytes()
inventory = json.loads(inventory_bytes)
runtime = json.loads(runtime_bytes)
content = Path(unreal.Paths.project_content_dir())

def package_hash(path):
    if not path.startswith("/Game/"):
        raise ValueError("Unexpected source package: " + path)
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())

paths = {"main": runtime["classes"]["main"]["class"]}
paths.update({name: row["class"] for name, row in inventory["classes"].items()})
hashes = {path: package_hash(path) for path in paths.values()}
classes = {}
for name, path in paths.items():
    row = json.loads(unreal.AlsLyraGraphLibrary.read_source_nodes(unreal.load_class(None, path)))
    if row["class"] != path or package_hash(path) != runtime["assetSha256"][name]:
        raise ValueError("Stale runtime graph/class: " + path)
    nodes = {node["nodeIndex"]: node for node in row["sources"]}
    if len(nodes) != len(row["sources"]):
        raise ValueError("Duplicate source identity")
    for node in nodes.values():
        if node["nodeIndex"] != row["nodeCount"] - 1 - node["propertyIndex"]:
            raise ValueError("Wrong compiled/property index conversion")
        for asset in [node["asset"], node["skeleton"]] + [s["animation"] for s in node.get("samples", [])]:
            if asset and asset not in hashes:
                hashes[asset] = package_hash(asset)
    for graph in row["graphs"]:
        if len(set(graph["players"])) != len(graph["players"]) or any(i not in nodes for i in graph["players"]):
            raise ValueError("Invalid compiled graph source ownership: " + graph["name"])
    classes[name] = row
    unreal.log(f"LYRA_SOURCE_CLASS_OK name={name} sources={len(nodes)} callbacks={len(row['callbacks'])}")
if any(package_hash(path) != value for path, value in hashes.items()):
    raise ValueError("Source node read changed a package")

# Report resource closure separately: a bound default is not proof that a
# dynamically chosen sequence or BlendSpace sample has already been exported.
logical_bytes = (root / "logical_controls/catalog.json").read_bytes()
payload = {"schemaVersion": 1, "inventorySha256": sha(inventory_bytes),
           "runtimeGraphSha256": sha(runtime_bytes), "logicalCatalogSha256": sha(logical_bytes),
           "assetSha256": hashes, "classes": classes}
target = root / "source_nodes.json"
if target.exists():
    if json.loads(target.read_bytes()) != payload:
        raise ValueError("Existing source node inventory differs")
else:
    target.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log(f"LYRA_SOURCE_NODES_OK classes={len(classes)} sources={sum(len(c['sources']) for c in classes.values())} packages={len(hashes)} assets_saved=0")

"""Read compiled animation-interface signatures and linked-instance policies."""
import hashlib
import json
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
inventory_bytes = (root / "linked_layer_inventory.json").read_bytes()
inventory = json.loads(inventory_bytes)
prefix = "/Game/Characters/Heroes/Mannequin/Animations/"
paths = {"interface": prefix + "LinkedLayers/ALI_ItemAnimLayers.ALI_ItemAnimLayers_C",
         "main": prefix + "ABP_Mannequin_Base.ABP_Mannequin_Base_C"}
paths.update({name: row["class"] for name, row in inventory["classes"].items()})
classes = {}
hashes = {}
project = Path(unreal.Paths.project_dir())
for name, path in paths.items():
    cls = unreal.load_class(None, path)
    if cls is None:
        raise RuntimeError("Missing compiled animation class: " + path)
    row = json.loads(unreal.AlsLinkedLayerLibrary.read_linked_layer_class(cls))
    if row["class"] != path:
        raise RuntimeError("Wrong compiled animation class")
    classes[name] = row
    asset = project / "Content" / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
    hashes[name] = hashlib.sha256(asset.read_bytes()).hexdigest()
    unreal.log("LYRA_LINKED_CLASS_OK name=" + name + " functions=" + str(len(row["functions"])) +
               " nodes=" + str(len(row["linkedNodes"])))
hooks = {row["name"] for row in classes["interface"]["functions"]}
if len(hooks) != 14:
    raise RuntimeError("Lyra animation interface no longer contains exactly 14 hooks: " + repr(hooks))
for name in inventory["classes"]:
    functions = {row["name"]: row for row in classes[name]["functions"]}
    if not hooks.issubset(functions) or any(not functions[hook]["implemented"] for hook in hooks):
        raise RuntimeError("Incomplete compiled linked layer: " + name)
nodes = [node for node in classes["main"]["linkedNodes"] if node["interface"] == paths["interface"]]
if {node["layer"] for node in nodes} != hooks:
    raise RuntimeError("Main graph does not call all interface hooks")
payload = {"schemaVersion": 1, "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "assetSha256": hashes, "classes": classes}
output = root / "linked_layer_contracts.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing linked-layer contract differs")
else:
    output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_LINKED_CONTRACTS_OK classes=" + str(len(classes)) + " hooks=14 assets_saved=0")

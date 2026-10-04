"""Call real LinkAnimClassLayers and capture target-instance identities."""
import hashlib
import json
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
contracts_bytes = (root / "linked_layer_contracts.json").read_bytes()
contracts = json.loads(contracts_bytes)
classes = contracts["classes"]
names = ["unarmed", "unarmed", "pistol", "pistol", "rifle", "rifle", "unarmed", "unarmed"]
main = unreal.load_class(None, classes["main"]["class"])
mesh = unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny")
if mesh is None:
    raise RuntimeError("Missing Lyra Manny mesh")
layers = [unreal.load_class(None, classes[name]["class"]) for name in names]
result = json.loads(unreal.AlsLinkedLayerLibrary.read_linked_layer_binding_sequence(main, mesh, layers))
unreal.log("LYRA_LINKED_BINDING_OBSERVATION " + json.dumps([
    {"class": step["class"], "linkedInstances": step["linkedInstances"],
     "nodes": len(step["nodes"]), "owners": sorted({node["owner"] for node in step["nodes"]})}
    for step in result["steps"]], separators=(",", ":")))
if len(result["steps"]) != len(names):
    raise RuntimeError("Incomplete native linked-layer binding sequence")
for index, step in enumerate(result["steps"]):
    if step["linkedInstances"] != 1 or len(step["nodes"]) != 14:
        raise RuntimeError("Unexpected native linked-instance ownership")
    owners = {node["owner"] for node in step["nodes"]}
    if len(owners) != 1 or {node["class"] for node in step["nodes"]} != {step["class"]}:
        raise RuntimeError("Incorrect native linked instance targets")
    if index > 0:
        old = result["steps"][index - 1]
        same_owner = step["nodes"][0]["owner"] == old["nodes"][0]["owner"]
        if same_owner != (step["class"] == old["class"]):
            raise RuntimeError("Unexpected native same-class relink behavior")
payload = {"schemaVersion": 1, "contractsSha256": hashlib.sha256(contracts_bytes).hexdigest(),
           "nativeOperator": "UAnimInstance.LinkAnimClassLayers", "profiles": names, "result": result}
output = root / "linked_layer_binding_native.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing linked-layer binding oracle differs")
else:
    output.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
unreal.log("LYRA_LINKED_BINDING_NATIVE_OK steps=8 nodes=14 owners=4 same_class_reuse=4 assets_saved=0")

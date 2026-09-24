"""Read compiled identities/defaults for the original graph export, without asset writes."""
import hashlib
import json
import os
from pathlib import Path
import unreal

source = Path(os.environ["ALS_REFACTORED_LAYERING_GRAPHS"])
output = Path(os.environ["ALS_REFACTORED_LAYERING_INVENTORY"])
if not source.is_absolute() or not output.is_absolute() or source == output:
    raise ValueError("Distinct absolute input/output paths required")
data = source.read_bytes()
graphs = json.loads(data)
if graphs["schemaVersion"] != 1 or len(graphs["blueprints"]) != 4:
    raise ValueError("Unsupported graph inventory")
rows = []
for graph in graphs["blueprints"]:
    asset = unreal.load_asset(graph["source"])
    if not isinstance(asset, unreal.AnimBlueprint):
        raise ValueError("Missing AnimBlueprint: " + graph["source"])
    row = json.loads(unreal.AlsAnimationGraphLibrary.read_compiled_animation_graph(asset))
    if row["source"] != graph["source"] or row["compiledPropertyCount"] <= 0:
        raise ValueError("Invalid compiled graph: " + graph["source"])
    rows.append(row)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "graphsSha256": hashlib.sha256(data).hexdigest(),
                             "blueprints": rows}, separators=(",", ":"), allow_nan=False) + "\n",
                  encoding="utf-8", newline="\n")
unreal.log("ALS_REFACTORED_LAYERING_INVENTORY_OK assets=4 assets_saved=0")
if os.environ.get("ALS_REFACTORED_LAYERING_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

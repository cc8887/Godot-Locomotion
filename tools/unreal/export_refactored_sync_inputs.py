"""Read complete original Sequence/BlendSpace timing and marker resources."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2] / "assets/config"
catalog_bytes = (root / "refactored_animation_sources.json").read_bytes()
catalog = json.loads(catalog_bytes)
rows = []
for entry in catalog["assets"]:
    if entry["class"] not in ("AnimSequence", "BlendSpace", "BlendSpace1D"):
        continue
    asset = unreal.load_asset(entry["source"])
    row = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(asset))
    if row["source"] != entry["source"]:
        raise ValueError("Foreign sync asset")
    rows.append(row)
output = Path(os.environ["ALS_REFACTORED_SYNC_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "catalogSha256": hashlib.sha256(catalog_bytes).hexdigest(), "assets": rows},
                            separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_SYNC_INPUTS_OK assets={len(rows)} markers={sum(len(r.get('markers', [])) for r in rows)} assets_saved=0")
if os.environ.get("ALS_REFACTORED_SYNC_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

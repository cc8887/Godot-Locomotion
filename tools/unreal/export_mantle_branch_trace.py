"""Actual native montage/ALS state trace; transient actors, no asset saves."""
import os
import json
import hashlib
from pathlib import Path
import unreal

output = Path(os.environ["ALS_MANTLE_BRANCH_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.AlsAnimationGraphLibrary.export_mantling_branch_trace(str(output)):
    raise RuntimeError("Native mantle branch trace failed")
data = json.loads(output.read_text(encoding="utf-8"))
source = Path(__file__).parents[2] / "assets/config/refactored_mantle_animation_inputs.json"
data["animationInputsSha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
if len(data["traces"]) != 126 or any(not t["frames"] or t["frames"][-1]["exists"] for t in data["traces"]):
    raise RuntimeError("Incomplete native trace")
output.write_text(json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_MANTLE_BRANCH_OK traces=126 assets_saved=0")
if os.environ.get("ALS_MANTLE_BRANCH_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

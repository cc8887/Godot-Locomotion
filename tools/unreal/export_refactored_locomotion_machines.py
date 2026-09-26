"""Read linked Grounded/Locomotion state machines without modifying UE assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_LOCOMOTION_MACHINES_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
items = []
for name, count in (("Grounded", 1), ("Locomotion", 2)):
    source = f"/ALS/ALS/Character/AnimationInstances/AB_Als_{name}.AB_Als_{name}"
    blueprint = unreal.load_asset(source)
    if not blueprint:
        raise RuntimeError("Cannot load " + source)
    item = json.loads(unreal.AlsAnimationGraphLibrary.read_baked_state_machines(blueprint))
    if item["source"] != source or len(item["bakedMachines"]) != count:
        raise RuntimeError("Unexpected linked movement machine closure")
    items.append(item)
value = {"schemaVersion": 1, "catalogSha256": hashlib.sha256(
    (root / "assets/config/refactored_animation_sources.json").read_bytes()).hexdigest(), "graphs": items}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_LOCOMOTION_MACHINES_OK graphs=2 machines=3 assets_saved=0")
if os.environ.get("ALS_LOCOMOTION_MACHINES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

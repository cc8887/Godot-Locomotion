"""Read original weapon baked states, transitions, curves and notify metadata."""
import hashlib
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_WEAPON_MACHINES_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
catalog = Path(__file__).parents[2] / "assets/config/refactored_animation_sources.json"
items = []
for kind in ("Bow", "PistolOneHanded", "PistolTwoHanded", "Rifle"):
    source = f"/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_{kind}.AB_Als_{kind}"
    blueprint = unreal.load_asset(source)
    if not blueprint:
        raise RuntimeError("Cannot load " + source)
    item = json.loads(unreal.AlsAnimationGraphLibrary.read_baked_state_machines(blueprint))
    if item["source"] != source or len(item["bakedMachines"]) != 1:
        raise RuntimeError("Unexpected weapon machine closure")
    items.append(item)
value = {"schemaVersion": 1, "catalogSha256": hashlib.sha256(catalog.read_bytes()).hexdigest(), "weapons": items}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_WEAPON_MACHINES_OK weapons=4 assets_saved=0")
if os.environ.get("ALS_WEAPON_MACHINES_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

"""Read full precision movement settings/curves from the original Parent defaults."""
import hashlib
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_MOVEMENT_SETTINGS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
root = Path(__file__).parents[2]
catalog = root / "assets/config/refactored_animation_sources.json"
animation_class = unreal.load_class(None, "/ALS/ALS/Character/AB_Als.AB_Als_C")
if not animation_class:
    raise RuntimeError("Original Parent class missing")
settings = unreal.get_default_object(animation_class).get_editor_property("settings")
value = json.loads(unreal.AlsAnimationGraphLibrary.read_movement_settings(settings))
value["catalogSha256"] = hashlib.sha256(catalog.read_bytes()).hexdigest()
value["animationClass"] = animation_class.get_path_name()
if value["source"] != "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default":
    raise RuntimeError("Unexpected original settings binding")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_MOVEMENT_SETTINGS_OK curves=%d assets_saved=0" % len(value["curves"]))
if os.environ.get("ALS_MOVEMENT_SETTINGS_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

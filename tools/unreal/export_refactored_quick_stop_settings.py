"""Read full-precision QuickStop settings from the original Parent CDO; save no assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_QUICK_STOP_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
root = Path(__file__).parents[2]
animation_class = unreal.load_class(None, "/ALS/ALS/Character/AB_Als.AB_Als_C")
settings = unreal.get_default_object(animation_class).get_editor_property("settings")
if settings.get_path_name() != "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default":
    raise RuntimeError("Unexpected Parent settings")
transitions = settings.get_editor_property("transitions")
rate = transitions.get_editor_property("quick_stop_play_rate")
sequences = {}
for stance in ("standing", "crouching"):
    for side in ("left", "right"):
        key = stance + "_" + side
        asset = transitions.get_editor_property(key + "_sequence")
        if not asset:
            raise RuntimeError("Missing transition: " + key)
        sequences[key] = asset.get_path_name()
value = {"schemaVersion": 1, "source": settings.get_path_name(), "animationClass": animation_class.get_path_name(),
         "catalogSha256": hashlib.sha256((root / "assets/config/refactored_animation_sources.json").read_bytes()).hexdigest(),
         "blendIn": float(transitions.get_editor_property("quick_stop_blend_in_duration")),
         "blendOut": float(transitions.get_editor_property("quick_stop_blend_out_duration")),
         "startTime": float(transitions.get_editor_property("quick_stop_start_time")),
         "playRate": [float(rate.get_editor_property("x")), float(rate.get_editor_property("y"))],
         "sequences": sequences}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_QUICK_STOP_SETTINGS_OK sequences=4 assets_saved=0")
if os.environ.get("ALS_QUICK_STOP_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

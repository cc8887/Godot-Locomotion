"""Read the character's actual settings binding; do not save or modify assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_CHARACTER_SETTINGS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
character_class = unreal.load_class(None, "/ALS/ALS/Character/B_Als_Character.B_Als_Character_C")
if not character_class:
    raise RuntimeError("Original character class missing")
character = unreal.get_default_object(character_class)
settings = character.get_editor_property("settings")
if settings.get_path_name() != "/ALS/ALS/Data/Character/CS_Als_Default.CS_Als_Default":
    raise RuntimeError("Unexpected character settings binding")
value = {"schemaVersion": 1, "characterClass": character_class.get_path_name(),
         "source": settings.get_path_name(),
         "movingSpeedThreshold": settings.get_editor_property("moving_speed_threshold"),
         "teleportDistanceThreshold": character.get_editor_property("mesh").get_editor_property("teleport_distance_threshold"),
         "animationSettingsSha256": hashlib.sha256((root / "assets/config/refactored_movement_settings.json").read_bytes()).hexdigest()}
if value["movingSpeedThreshold"] < 0 or value["teleportDistanceThreshold"] < 0:
    raise RuntimeError("Invalid moving threshold")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_CHARACTER_SETTINGS_OK assets_saved=0")
if os.environ.get("ALS_CHARACTER_SETTINGS_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

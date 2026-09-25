"""Read original turn/rotate/dynamic-transition properties, without saving assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_REST_SETTINGS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
root = Path(__file__).parents[2]
animation_class = unreal.load_class(None, "/ALS/ALS/Character/AB_Als.AB_Als_C")
settings = unreal.get_default_object(animation_class).get_editor_property("settings")
if settings.get_path_name() != "/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default":
    raise RuntimeError("Unexpected Parent settings")

def scalar(owner, name):
    return float(owner.get_editor_property(name))

def vector(owner, name):
    v = owner.get_editor_property(name)
    return [scalar(v, "x"), scalar(v, "y")]

def path(owner, name):
    asset = owner.get_editor_property(name)
    if not asset:
        raise RuntimeError("Missing original resource: " + name)
    return asset.get_path_name()

rotate = settings.get_editor_property("rotate_in_place")
turn = settings.get_editor_property("turn_in_place")
dynamic = settings.get_editor_property("dynamic_transitions")
turns = []
for stance in ("standing", "crouching"):
    for angle in (90, 180):
        for side in ("left", "right"):
            field = "%s_turn%d_%s" % (stance, angle, side)
            item = turn.get_editor_property(field)
            turns.append({"binding": field, "source": item.get_path_name(), "sequence": path(item, "sequence"),
                          "playRate": scalar(item, "play_rate"), "animatedTurnAngle": scalar(item, "animated_turn_angle"),
                          "scalePlayRate": bool(item.get_editor_property("scale_play_rate_by_animated_turn_angle"))})
value = {"schemaVersion": 1, "source": settings.get_path_name(), "animationClass": animation_class.get_path_name(),
         "catalogSha256": hashlib.sha256((root / "assets/config/refactored_animation_sources.json").read_bytes()).hexdigest(),
         "rotate": {"yawThreshold": scalar(rotate, "view_yaw_angle_threshold"),
                    "firstPersonYawThreshold": scalar(rotate, "first_person_view_yaw_angle_threshold"),
                    "referenceYawSpeed": vector(rotate, "reference_view_yaw_speed"), "playRate": vector(rotate, "play_rate")},
         "turn": {"yawThreshold": scalar(turn, "view_yaw_angle_threshold"), "yawSpeedThreshold": scalar(turn, "view_yaw_speed_threshold"),
                  "activationDelay": vector(turn, "view_yaw_angle_to_activation_delay"), "turn180Threshold": scalar(turn, "turn180_angle_threshold"),
                  "blendDuration": scalar(turn, "blend_duration"), "assets": turns},
         "dynamic": {"distanceThreshold": scalar(dynamic, "foot_lock_distance_threshold"), "blendDuration": scalar(dynamic, "blend_duration"),
                     "playRate": scalar(dynamic, "play_rate"),
                     "sequences": {stance + "_" + side: path(dynamic, stance + "_" + side + "_sequence")
                                   for stance in ("standing", "crouching") for side in ("left", "right")}}}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
unreal.log("ALS_REST_SETTINGS_OK turns=8 dynamic=4 assets_saved=0")
if os.environ.get("ALS_REST_SETTINGS_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

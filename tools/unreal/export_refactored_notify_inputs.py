"""Read the original catalog's complete Sequence/Montage notify closure. No saves.

Audio payload settings are preserved as metadata; no audio asset export is performed.
"""
import hashlib
import json
import os
import runpy
from pathlib import Path

import unreal

root = Path(__file__).parents[2] / "assets/config"
catalog_bytes = (root / "refactored_animation_sources.json").read_bytes()
catalog = json.loads(catalog_bytes)
read_asset = runpy.run_path(str(Path(__file__).with_name("export_action_notify_inputs.py")))["read_asset"]


def payload(row):
    obj = unreal.load_object(None, row["stateObject"] or row["notifyObject"])
    get = obj.get_editor_property

    def tag(name):
        return str(get(name).get_editor_property("tag_name"))

    def path(name):
        value = get(name)
        return value.get_path_name() if value is not None else ""

    kind = row["class"]
    if kind == "/Script/ALS.AlsAnimNotify_FootstepEffects":
        result = {key: get(key) for key in (
            "skip_effects_when_in_air", "spawn_sound", "sound_volume_multiplier", "sound_pitch_multiplier",
            "ignore_footstep_sound_block_curve", "spawn_decal", "spawn_particle_system")}
        result.update(footstep_effects_settings=path("footstep_effects_settings"),
                      foot_bone=get("foot_bone").name, sound_type=get("sound_type").name)
        return result
    if kind == "/Script/ALS.AlsAnimNotify_SetGroundedEntryMode":
        return {"grounded_entry_mode": tag("grounded_entry_mode")}
    if kind == "/Script/ALS.AlsAnimNotifyState_SetLocomotionAction":
        return {"locomotion_action": tag("locomotion_action")}
    if kind == "/Script/ALS.AlsAnimNotifyState_EarlyBlendOut":
        result = {key: get(key) for key in (
            "blend_out_duration", "check_input", "check_locomotion_mode", "check_rotation_mode", "check_stance")}
        result.update({key: tag(key) for key in ("locomotion_mode_equals", "rotation_mode_equals", "stance_equals")})
        return result
    if kind == "/Script/ALS.AlsAnimNotifyState_SetRootMotionScale":
        return {"translation_scale": get("translation_scale")}
    if kind == "/Script/ALSCamera.AlsAnimNotify_CameraShake":
        return {"camera_shake_class": path("camera_shake_class"), "camera_shake_scale": get("camera_shake_scale")}
    raise ValueError("Unbound original notify class: " + kind)


rows = []
for entry in sorted(catalog["assets"], key=lambda item: item["source"]):
    if entry["class"] not in ("AnimSequence", "AnimMontage"):
        continue
    row = read_asset(entry["source"])
    if row["path"] != entry["source"]:
        raise ValueError("Foreign notify source")
    asset = unreal.load_asset(row["path"])
    row.update(sourceSha256=entry["sha256"], length=asset.get_play_length())
    for event in row["notifies"]:
        event["payload"] = payload(event)
    rows.append(row)

output = Path(os.environ["ALS_REFACTORED_NOTIFY_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "catalogSha256": hashlib.sha256(catalog_bytes).hexdigest(),
                             "assets": rows}, separators=(",", ":"), allow_nan=False) + "\n",
                  encoding="utf-8", newline="\n")
unreal.log(f"ALS_REFACTORED_NOTIFY_INPUTS_OK assets={len(rows)} notifies={sum(len(r['notifies']) for r in rows)} assets_saved=0")
if os.environ.get("ALS_REFACTORED_NOTIFY_QUIT") == "1":
    ticks = 0

    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()

    handle = unreal.register_slate_post_tick_callback(finish)

"""Read Roll Montage/sequence notify identities and policies without saving assets."""
import json
import os
import re
import struct
from pathlib import Path

import unreal


def f32(value):
    return struct.unpack("f", struct.pack("f", value))[0]


def read_asset(path):
    asset = unreal.load_asset(path)
    if asset is None:
        raise RuntimeError("Missing action notify asset: " + path)
    rows = []
    library = unreal.AnimationLibrary
    for index, event in enumerate(library.get_animation_notify_events(asset)):
        get = event.get_editor_property
        native = event.export_text()

        def hidden(name):
            match = re.search(r"(?:^|[,(])" + re.escape(name) + r"=([^,()]+)", native)
            if match is None:
                raise RuntimeError("Missing native notify field " + name + ": " + native)
            return f32(float(match.group(1)))

        notify = get("notify")
        state = get("notify_state_class")
        if (notify is None) == (state is None):
            raise RuntimeError("Expected exactly one class-based notify object")
        obj = state if state is not None else notify
        trigger = library.get_anim_notify_event_trigger_time(event)
        duration = library.get_anim_notify_event_duration(event)
        offset = hidden("TriggerTimeOffset")
        end_offset = hidden("EndTriggerTimeOffset")
        # FAnimNotifyEvent::GetEndTriggerTime (AnimTypes.cpp) includes both offsets.
        # Match each native float addition rather than a double-precision sum.
        end = f32(f32(trigger + duration) + end_offset) if state is not None else trigger
        rows.append({
            "index": index, "track": int(hidden("TrackIndex")), "name": str(get("notify_name")),
            "notifyObject": notify.get_path_name() if notify is not None else "",
            "stateObject": state.get_path_name() if state is not None else "",
            "stateBehaviorFlags": state.get_editor_property("notify_state_behavior_flags") if state is not None else 0,
            "class": obj.get_class().get_path_name(), "time": f32(trigger - offset), "duration": duration,
            "triggerOffset": offset, "endTriggerOffset": end_offset, "triggerTime": trigger, "endTriggerTime": end,
            "weightThreshold": get("trigger_weight_threshold"), "chance": get("notify_trigger_chance"),
            "filterType": get("notify_filter_type").value, "filterLod": get("notify_filter_lod"),
            "tickMode": get("montage_tick_type").value, "filterViaRequest": get("can_be_filtered_via_request"),
            "onDedicatedServer": get("trigger_on_dedicated_server"), "onFollower": get("trigger_on_follower"),
        })
    return {"path": asset.get_path_name(), "notifies": rows}


def export():
    base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/"
    sequence = read_asset(base + "ALS_N_LandRoll_F")
    montage = read_asset(base + "ALS_N_LandRoll_F_Montage_Default")
    output = Path(os.environ["ALS_ACTION_NOTIFY_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_ACTION_NOTIFY_OUTPUT must be absolute")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"schemaVersion": 1,
        "sequences": {"notifySchemaVersion": 1, "syncAssets": [sequence]},
        "montages": {"notifySchemaVersion": 1, "syncAssets": [montage]},
    }, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_ACTION_NOTIFY_INPUTS_OK assets=2 notifies=" +
               str(len(sequence["notifies"]) + len(montage["notifies"])) + " assets_saved=0")


if __name__ == "__main__":
    export()
# The Python commandlet exits after the script; ExecutePythonScript schedules
# QUIT_EDITOR on the next tick. Do not close the editor inside this Python call.

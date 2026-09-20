"""Read original Turn sequence notify policies; never save or modify assets."""
import json
import os
import re
from pathlib import Path

import unreal


assets = []
base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/"
library = unreal.AnimationLibrary
for stance in ("N", "CLF"):
    for direction in ("L90", "R90", "L180", "R180"):
        name = "ALS_" + stance + "_TurnIP_" + direction
        asset = unreal.load_asset(base + name)
        if asset is None:
            raise RuntimeError("Missing turn sequence: " + name)
        rows = []
        for index, event in enumerate(library.get_animation_notify_events(asset)):
            get = event.get_editor_property
            native = event.export_text()
            def hidden(name):
                match = re.search(r"(?:^|[,(])" + re.escape(name) + r"=([^,()]+)", native)
                if match is None:
                    raise RuntimeError("Missing native notify field " + name + ": " + native)
                return float(match.group(1))
            notify = get("notify")
            state = get("notify_state_class")
            # Current Turn sequences only contain instant class-based notifies.
            # Do not infer NotifyState end times or unexposed behavior flags.
            if notify is None or state is not None:
                raise RuntimeError("Turn notify export requires an instant class-based event")
            trigger = library.get_anim_notify_event_trigger_time(event)
            offset = hidden("TriggerTimeOffset")
            rows.append({
                "index": index, "track": int(hidden("TrackIndex")), "name": str(get("notify_name")),
                "notifyObject": notify.get_path_name(), "stateObject": "", "stateBehaviorFlags": 0,
                "class": notify.get_class().get_path_name(), "time": trigger - offset,
                "duration": library.get_anim_notify_event_duration(event),
                "triggerOffset": offset, "endTriggerOffset": hidden("EndTriggerTimeOffset"),
                "triggerTime": trigger, "endTriggerTime": trigger,
                "weightThreshold": get("trigger_weight_threshold"), "chance": get("notify_trigger_chance"),
                "filterType": get("notify_filter_type").value, "filterLod": get("notify_filter_lod"),
                "tickMode": get("montage_tick_type").value, "filterViaRequest": get("can_be_filtered_via_request"),
                "onDedicatedServer": get("trigger_on_dedicated_server"), "onFollower": get("trigger_on_follower"),
            })
        assets.append({"path": asset.get_path_name(), "notifies": rows})
output = Path(os.environ["ALS_TURN_NOTIFY_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_TURN_NOTIFY_OUTPUT must be absolute")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"notifySchemaVersion": 1, "syncAssets": assets}, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_TURN_NOTIFY_INPUTS_OK assets=8 notifies=" + str(sum(len(a["notifies"]) for a in assets)) + " assets_saved=0")
if os.environ.get("ALS_TURN_NOTIFY_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

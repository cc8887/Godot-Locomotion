"""Read both grounded additive transitions, their playback metadata and notifies."""
import importlib.util
import json
import os
from pathlib import Path

import unreal

helper = Path(__file__).with_name("export_action_notify_inputs.py")
spec = importlib.util.spec_from_file_location("als_transition_notify_reader", helper)
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
output = Path(os.environ["ALS_TRANSITION_NOTIFY_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute transition notify output required")
base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions/"
assets = []
for suffix in ("L", "R"):
    name = "ALS_N_Transition_" + suffix
    row = reader.read_asset(base + name + "." + name)
    asset = unreal.load_asset(row["path"])
    row["rateScale"] = asset.get_editor_property("rate_scale")
    row["length"] = unreal.AnimationLibrary.get_sequence_length(asset)
    assets.append(row)
payload = {"schemaVersion": 1,
           "source": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP",
           "notifySchemaVersion": 1, "syncAssets": assets}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log("ALS_TRANSITION_NOTIFY_INPUTS_OK assets=2 notifies=" + str(sum(len(a["notifies"]) for a in assets)) + " assets_saved=0")
if os.environ.get("ALS_TRANSITION_NOTIFY_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

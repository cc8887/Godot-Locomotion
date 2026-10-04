"""Record item layer controls and compare inherited Pistol values with Unarmed."""

import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["LYRA_OUTPUT_ROOT"])
inventory = json.loads((root / "linked_layer_inventory.json").read_text(encoding="utf-8"))
unarmed = json.loads((root / "unarmed_layer_defaults.json").read_text(encoding="utf-8"))
profile_name = os.environ.get("LYRA_SPECIAL_PROFILE", "pistol")
if profile_name not in ("pistol", "rifle"):
    raise ValueError("Unsupported item layer profile: " + profile_name)
class_path = inventory["classes"][profile_name]["class"]
cls = unreal.load_class(None, class_path)
if not cls:
    raise RuntimeError(profile_name + " layer class is missing: " + class_path)
defaults = unreal.get_default_object(cls)
properties = {}
for name in ("PlayRateClampStartsPivots", "PlayRateClampCycle",
             "StrideWarpingBlendInDurationScaled", "StrideWarpingBlendInStartOffset",
             "LocomotionDistanceCurveName"):
    value = defaults.get_editor_property(name)
    if isinstance(value, unreal.Vector2D):
        properties[name] = [value.x, value.y]
    elif isinstance(value, (float, int, str)):
        properties[name] = value
    else:
        properties[name] = str(value)
if profile_name == "pistol" and properties != unarmed["layers"]["unarmed"]["properties"]:
    raise RuntimeError("Pistol locomotion controls differ from Unarmed: " + str(properties))
output = root / (profile_name + "_layer_defaults.json")
payload = {"schemaVersion": 1, "class": class_path, "properties": properties}
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing " + profile_name + " defaults differ")
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_ITEM_DEFAULTS_OK profile=" + profile_name + " class=" + class_path)

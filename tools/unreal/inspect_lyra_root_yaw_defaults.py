"""Read Lyra's root-yaw defaults without changing Unreal assets."""

import json
import os
from pathlib import Path

import unreal


output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not output_root.is_absolute():
    raise ValueError("Lyra output root must be absolute")

class_path = (
    "/Game/Characters/Heroes/Mannequin/Animations/"
    "ABP_Mannequin_Base.ABP_Mannequin_Base_C"
)
cls = unreal.load_class(None, class_path)
if cls is None:
    raise RuntimeError("Missing Lyra parent AnimBP: " + class_path)

defaults = unreal.get_default_object(cls)
standing = defaults.get_editor_property("RootYawOffsetAngleClamp")
crouched = defaults.get_editor_property("RootYawOffsetAngleClampCrouched")
payload = {
    "schemaVersion": 1,
    "class": class_path,
    "enableRootYawOffset": bool(defaults.get_editor_property("bEnableRootYawOffset")),
    "angleClamp": [standing.x, standing.y],
    "angleClampCrouched": [crouched.x, crouched.y],
    "initialMode": str(defaults.get_editor_property("RootYawOffsetMode")),
}
output = output_root / "root_yaw_defaults.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra root-yaw defaults differ: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_ROOT_YAW_DEFAULTS_OK " + json.dumps(payload, sort_keys=True))

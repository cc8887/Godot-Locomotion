"""Read Lyra's base and Unarmed linked-layer defaults without saving UE assets."""

import json
import os
from pathlib import Path

import unreal


output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not output_root.is_absolute():
    raise ValueError("Lyra output root must be absolute")

classes = {
    "base": "/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase.ABP_ItemAnimLayersBase_C",
    "unarmed": "/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed/ABP_UnarmedAnimLayers.ABP_UnarmedAnimLayers_C",
}
names = (
    "PlayRateClampStartsPivots", "PlayRateClampCycle",
    "StrideWarpingBlendInDurationScaled", "StrideWarpingBlendInStartOffset",
    "LocomotionDistanceCurveName",
)
rows = {}
for label, path in classes.items():
    cls = unreal.load_class(None, path)
    if cls is None:
        raise RuntimeError("Missing Lyra animation class: " + path)
    defaults = unreal.get_default_object(cls)
    properties = {}
    for name in names:
        value = defaults.get_editor_property(name)
        if isinstance(value, unreal.Vector2D):
            properties[name] = [value.x, value.y]
        elif isinstance(value, (float, int, str)):
            properties[name] = value
        else:
            properties[name] = str(value)
    rows[label] = {"class": path, "properties": properties}
    unreal.log("LYRA_UNARMED_LAYER_DEFAULTS_CLIP_OK class=" + label +
               " clamp=" + str(properties["PlayRateClampStartsPivots"]))

payload = {"schemaVersion": 1, "layers": rows}
output = output_root / "unarmed_layer_defaults.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra layer defaults differ: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_UNARMED_LAYER_DEFAULTS_OK layers=" + str(len(rows)))

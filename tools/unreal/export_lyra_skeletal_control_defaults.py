"""Read current Lyra hand/foot control defaults and both skeleton target layouts."""
import hashlib
import json
import os
from pathlib import Path

import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("An existing absolute LYRA_OUTPUT_ROOT is required")
inventory_bytes = (root / "linked_layer_inventory.json").read_bytes()
inventory = json.loads(inventory_bytes)
main_path = "/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base_C"
main = unreal.get_default_object(unreal.load_class(None, main_path))
main_defaults = {name: bool(main.get_editor_property(name)) for name in ("UseFootPlacement", "EnableControlRig")}
profiles = {}
for name, row in inventory["classes"].items():
    cdo = unreal.get_default_object(unreal.load_class(None, row["class"]))
    profiles[name] = {key: float(cdo.get_editor_property(key))
                      for key in ("Hand FKWeight", "HandIK_Right_Alpha", "HandIK_Left_Alpha")}
layouts = {}
for name, asset in (("source", "/Game/Characters/Heroes/Mannequin/Meshes/SK_Mannequin.SK_Mannequin"),
                    ("target", "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton")):
    skeleton = unreal.load_asset(asset)
    layout = json.loads(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
    layouts[name] = {"asset": asset, "layout": layout}
payload = {"schemaVersion": 1, "mainClass": main_path, "mainDefaults": main_defaults,
           "inventorySha256": hashlib.sha256(inventory_bytes).hexdigest(),
           "profiles": profiles, "skeletons": layouts}
output = root / "skeletal_control_defaults.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing skeletal controls contract differs")
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_SKELETAL_CONTROL_DEFAULTS_OK " + json.dumps({"main": main_defaults, "profiles": profiles}, sort_keys=True))

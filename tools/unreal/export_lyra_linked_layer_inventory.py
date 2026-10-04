"""Inventory the assets actually bound by Lyra's item linked-layer classes."""

import json
import os
from pathlib import Path

import unreal


output_root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not output_root.is_absolute() or not output_root.is_dir():
    raise ValueError("LYRA_OUTPUT_ROOT must be an existing absolute directory")

root = "/Game/Characters/Heroes/Mannequin/Animations"
classes = {
    "base": root + "/LinkedLayers/ABP_ItemAnimLayersBase",
    "unarmed": root + "/Locomotion/Unarmed/ABP_UnarmedAnimLayers",
    "unarmed_feminine": root + "/Locomotion/Unarmed/ABP_UnarmedAnimLayers_Feminine",
    "pistol": root + "/Locomotion/Pistol/ABP_PistolAnimLayers",
    "pistol_feminine": root + "/Locomotion/Pistol/ABP_PistolAnimLayers_Feminine",
    "rifle": root + "/Locomotion/Rifle/ABP_RifleAnimLayers",
    "rifle_feminine": root + "/Locomotion/Rifle/ABP_RifleAnimLayers_Feminine",
    "shotgun": root + "/Locomotion/Shotgun/ABP_ShotgunAnimLayers",
    "shotgun_feminine": root + "/Locomotion/Shotgun/ABP_ShotgunAnimLayers_Feminine",
}
asset_properties = (
    "Idle_ADS", "Idle_Hipfire", "Jump_Start", "Jump_StartLoop", "Jump_Apex",
    "Jump_FallLoop", "Jump_FallLand", "Jump_RecoveryAdditive", "Crouch_Idle",
    "Crouch_Idle_Entry", "Crouch_Idle_Exit", "Aim_HipFirePose",
    "Aim_HipFirePose_Crouch", "LeftHandPose_Override", "IdleAimOffset",
    "RelaxedAimOffset", "TurnInPlace_Left", "TurnInPlace_Right",
    "Crouch_TurnInPlace_Left", "Crouch_TurnInPlace_Right",
)
cardinal_properties = (
    "Walk_Cardinals", "Crouch_Walk_Cardinals", "Jog_Cardinals",
    "Jog_Start_Cardinals", "Jog_Stop_Cardinals", "Jog_Pivot_Cardinals",
    "ADS_Start_Cardinals", "ADS_Stop_Cardinals", "ADS_Pivot_Cardinals",
    "Crouch_Start_Cardinals", "Crouch_Stop_Cardinals", "Crouch_Pivot_Cardinals",
)
scalar_properties = (
    "EnableLeftHandPoseOverride", "DisableHandIK",
    "RaiseWeaponAfterFiringWhenCrouched", "RaiseWeaponAfterFiringDuration",
)
cardinal_fields = {
    "forward": "Forward_12_1A70C7D74223ECC610B8E0B7CEA98DEB",
    "backward": "Backward_13_CF59C8D94DCBEFE2AF1A66ADEB6EE2AE",
    "left": "Left_14_A3A742054C2C89979F0DD685FCC7ED04",
    "right": "Right_15_77966216476637C79AA24E9C31ACA6C4",
}


def asset_path(value):
    if value is None:
        return None
    if not isinstance(value, (unreal.AnimSequence, unreal.BlendSpace)):
        raise RuntimeError("Unsupported Lyra layer asset type: " + str(type(value)))
    return value.get_path_name()


rows = {}
for label, path in classes.items():
    name = path.rsplit("/", 1)[-1]
    cls = unreal.load_class(None, path + "." + name + "_C")
    if cls is None:
        raise RuntimeError("Missing Lyra linked-layer class: " + path)
    defaults = unreal.get_default_object(cls)
    assets = {key: asset_path(defaults.get_editor_property(key)) for key in asset_properties}
    cardinals = {}
    for key in cardinal_properties:
        group = defaults.get_editor_property(key)
        cardinals[key] = {direction: asset_path(group.get_editor_property(field))
                          for direction, field in cardinal_fields.items()}
    scalars = {key: defaults.get_editor_property(key) for key in scalar_properties}
    rows[label] = {"class": cls.get_path_name(), "assets": assets,
                   "cardinals": cardinals, "scalars": scalars}
    unreal.log("LYRA_LAYER_INVENTORY_CLASS_OK name=" + label +
               " assets=" + str(sum(value is not None for value in assets.values())))

payload = {"schemaVersion": 1, "classes": rows}
output = output_root / "linked_layer_inventory.json"
if output.exists():
    if json.loads(output.read_text(encoding="utf-8")) != payload:
        raise RuntimeError("Existing Lyra linked-layer inventory differs: " + str(output))
else:
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
unreal.log("LYRA_LAYER_INVENTORY_OK classes=" + str(len(rows)))

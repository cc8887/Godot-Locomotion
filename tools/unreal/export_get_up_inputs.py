"""Read Roll and default front/back Get-up montages without saving UE assets."""
import json
import os
import re
import runpy
import tempfile
from pathlib import Path

import unreal


def export():
    output = Path(os.environ["ALS_GET_UP_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_GET_UP_OUTPUT must be absolute")
    read_asset = runpy.run_path(str(Path(__file__).with_name("export_action_notify_inputs.py")))["read_asset"]
    base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/"
    names = ("ALS_N_LandRoll_F_Montage_Default", "ALS_CLF_GetUp_Front_Montage_Default", "ALS_CLF_GetUp_Back_Montage_Default")
    assets, sequences, montages = [], {}, []
    for name in names:
        asset = unreal.load_asset(base + name)
        if asset is None:
            raise RuntimeError("Missing montage: " + name)
        get = asset.get_editor_property
        slots = get("slot_anim_tracks")
        if len(slots) != 1 or str(slots[0].get_editor_property("slot_name")) != "BaseLayer":
            raise RuntimeError("Unsupported action slots")
        segments = slots[0].get_editor_property("anim_track").get_editor_property("anim_segments")
        if len(segments) != 1:
            raise RuntimeError("Expected single-sequence action")
        sequence = asset.get_first_anim_reference()
        if sequence is None:
            raise RuntimeError("Missing action sequence")
        skeleton = get("skeleton")
        with tempfile.TemporaryDirectory(prefix="als-getup-skeleton-") as temporary:
            task = unreal.AssetExportTask()
            task.object = skeleton
            task.exporter = unreal.ObjectExporterT3D()
            task.filename = str(Path(temporary) / "skeleton.t3d")
            task.automated = True
            task.prompt = False
            if not unreal.Exporter.run_asset_export_task(task):
                raise RuntimeError("Skeleton export failed")
            data = Path(task.filename).read_bytes()
            native = data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")
        groups = [line for line in native.splitlines() if re.match(r"\s+SlotGroups\(", line) and '"BaseLayer"' in line]
        if len(groups) != 1:
            raise RuntimeError("Ambiguous BaseLayer group")
        group = re.search(r'GroupName="([^"]+)"', groups[0])
        blend_in, blend_out = asset.get_blend_in_args(), asset.get_blend_out_args()
        assets.append({
            "path": asset.get_path_name(), "length": asset.get_play_length(), "slot": 2,
            "in": blend_in.get_editor_property("blend_time"), "out": blend_out.get_editor_property("blend_time"),
            "inOption": blend_in.get_editor_property("blend_option").value,
            "outOption": blend_out.get_editor_property("blend_option").value,
            "trigger": get("blend_out_trigger_time"), "auto": get("enable_auto_blend_out"),
            "group": group.group(1) if group else "DefaultGroup", "rateScale": get("rate_scale"),
            "blendInMode": get("blend_mode_in").value, "blendOutMode": get("blend_mode_out").value,
            "blendProfiles": get("blend_profile_in") is not None or get("blend_profile_out") is not None,
            "customBlendCurves": blend_in.get_editor_property("custom_curve") is not None or blend_out.get_editor_property("custom_curve") is not None,
            "hasRootMotion": sequence.get_editor_property("enable_root_motion"),
        })
        sequences[sequence.get_path_name()] = read_asset(sequence.get_path_name())
        montages.append(read_asset(asset.get_path_name()))
    output.mkdir(parents=True, exist_ok=True)
    prefix = os.environ.get("ALS_GET_UP_PREFIX", "v4_action")
    if prefix not in ("v4_action", "v4_recovery_action"):
        raise ValueError("Unsupported output prefix")
    (output / (prefix + "_montage_inputs.json")).write_text(json.dumps({"schemaVersion": 1, "assets": assets}, indent=2) + "\n", encoding="utf-8")
    (output / (prefix + "_notify_inputs.json")).write_text(json.dumps({"schemaVersion": 1,
        "sequences": {"notifySchemaVersion": 1, "syncAssets": list(sequences.values())},
        "montages": {"notifySchemaVersion": 1, "syncAssets": montages}}, indent=2) + "\n", encoding="utf-8")
    unreal.log("ALS_GET_UP_INPUTS_OK montages=3 sequences=3 assets_saved=0")


if __name__ == "__main__":
    export()

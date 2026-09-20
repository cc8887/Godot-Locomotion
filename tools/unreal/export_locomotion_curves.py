"""Read native ALS curves without modifying or saving Unreal assets."""
import json
import os
import re
import tempfile
from pathlib import Path

import unreal


def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-curves-") as directory:
        task = unreal.AssetExportTask()
        task.object = asset
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "asset.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native property export failed")
        return Path(task.filename).read_text(encoding="utf-8-sig")


def export_curve(name):
    path = "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + name
    asset = unreal.load_asset(path)
    if not isinstance(asset, unreal.CurveFloat):
        raise RuntimeError("Missing float curve: " + path)
    # FloatCurve is protected in Python. UE's native property exporter preserves
    # key interpolation/tangents; reject unknown fields instead of approximating.
    exported = native_text(asset)
    curve = next(line.strip() for line in exported.splitlines() if line.strip().startswith("FloatCurve="))
    keys = []
    numeric = {"Time": "time", "Value": "value", "ArriveTangent": "arrive_tangent",
               "LeaveTangent": "leave_tangent", "ArriveTangentWeight": "arrive_tangent_weight",
               "LeaveTangentWeight": "leave_tangent_weight"}
    enums = {"InterpMode": "interp_mode", "TangentMode": "tangent_mode", "TangentWeightMode": "tangent_weight_mode"}
    for record in re.findall(r"\(([^()]*)\)", curve):
        key = {name: 0.0 for name in numeric.values()}
        key.update(interp_mode="RCIM_Linear", tangent_mode="RCTM_Auto", tangent_weight_mode="RCTWM_WeightedNone")
        for field in record.split(",") if record else []:
            field_name, value = field.split("=", 1)
            if field_name in numeric:
                key[numeric[field_name]] = float(value)
            elif field_name in enums:
                key[enums[field_name]] = value
            else:
                raise RuntimeError("Unsupported rich curve field: " + field_name)
        keys.append(key)
    if not keys:
        raise RuntimeError("No native rich curve keys: " + exported)
    lo, hi = asset.get_time_range()
    return {"name": name, "objectPath": asset.get_path_name(),
            "preInfinity": (re.search(r"PreInfinityExtrap=([A-Za-z_]+)", curve) or [None, "RCCE_Constant"])[1],
            "postInfinity": (re.search(r"PostInfinityExtrap=([A-Za-z_]+)", curve) or [None, "RCCE_Constant"])[1],
            "nativeText": exported,
            "keys": keys,
            "verification": [{"input": lo + (hi - lo) * i / 200,
                              "value": asset.get_float_value(lo + (hi - lo) * i / 200)}
                             for i in range(201)]}


output = Path(os.environ["ALS_LOCOMOTION_CURVES_OUTPUT"])
payload = {"schemaVersion": 1, "source": "ALS V4 ALS_AnimBP",
           "curves": [export_curve(name) for name in (
               "StrideBlend_N_Walk", "StrideBlend_N_Run", "DiagonalScaleAmount", "ChangeDirection")]}
profile = unreal.load_object(None, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton:ChangeDirection")
profile_text = native_text(profile)
payload["blendProfile"] = {"mode": "WeightFactor", "nativeText": profile_text,
    "entries": [{"bone": bone, "scale": float(scale)} for bone, scale in
                re.findall(r'BoneName="([^"]+)"\),BlendScale=([0-9.]+)', profile_text)]}
if "Mode=" in profile_text or len(payload["blendProfile"]["entries"]) != 15:
    raise RuntimeError("Unexpected blend profile mode or layout")
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_LOCOMOTION_CURVES_OK curves=4 assets_saved=0")

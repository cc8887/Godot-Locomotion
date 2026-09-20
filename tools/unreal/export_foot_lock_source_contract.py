"""Read the two versions' stop assets and native Standing graph; no asset saves."""
import json
import math
import os
from pathlib import Path
import unreal


def atom(t):
    p, q = t.translation, t.rotation
    return dict(position=[p.x, p.y, p.z], rotation=[q.x, q.y, q.z, q.w])


def export():
    output = Path(os.environ["ALS_FOOT_SOURCE_OUTPUT"])
    if not output.is_absolute() or output.exists():
        raise ValueError("A fresh absolute source audit directory is required")
    output.mkdir(parents=True)
    blueprint = unreal.load_asset("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Standing")
    if not isinstance(blueprint, unreal.AnimBlueprint):
        raise RuntimeError("Missing actual Refactored Standing AnimBP")
    task = unreal.AssetExportTask()
    task.object = blueprint
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(output / "refactored-standing.native.txt")
    task.automated = True
    task.prompt = False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Native Standing property export failed")
    machines = json.loads(unreal.AlsAnimationGraphLibrary.read_baked_state_machines(blueprint))
    (output / "refactored-standing-machines.json").write_text(json.dumps(machines, indent=2), encoding="utf-8")

    options = unreal.AnimPoseEvaluationOptions()
    options.evaluation_type = unreal.AnimDataEvalType.RAW
    options.should_retarget = True
    options.extract_root_motion = False
    options.incorporate_root_motion_into_pose = False
    options.retrieve_additive_as_full_pose = True
    options.evaluate_curves = True
    bones = ("pelvis", "ik_foot_l", "ik_foot_r")
    assets = []
    for version, prefix, names in (
        ("V4", "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions/",
         ("ALS_N_Stop_L_Down", "ALS_N_Stop_R_Down")),
        ("Refactored", "/ALS/ALS/Animations/Transitions/", ("A_Als_Stop_Left", "A_Als_Stop_Right"))):
        for name in names:
            sequence = unreal.load_asset(prefix + name)
            if not isinstance(sequence, unreal.AnimSequence):
                raise RuntimeError("Missing stop sequence: " + prefix + name)
            length = sequence.get_editor_property("data_model_interface").get_play_length()
            frames = []
            for index in range(math.ceil(length * 120) + 1):
                time = min(index / 120, length)
                pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(sequence, time, options)
                if not unreal.AnimPoseExtensions.is_valid(pose):
                    raise RuntimeError("Invalid source pose")
                available = {str(b).lower(): b for b in unreal.AnimPoseExtensions.get_bone_names(pose)}
                if not set(bones) <= available.keys():
                    raise RuntimeError("Missing target bones in " + name)
                curves = {str(n): unreal.AnimPoseExtensions.get_curve_weight(pose, n)
                          for n in unreal.AnimPoseExtensions.get_curve_names(pose)
                          if any(word in str(n).lower() for word in ("foot", "feet", "hip"))}
                frames.append(dict(time=time, curves=curves, component={bone: atom(
                    unreal.AnimPoseExtensions.get_bone_pose(pose, available[bone], unreal.AnimPoseSpaces.WORLD)) for bone in bones}))
            metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
            assets.append(dict(version=version, source=sequence.get_path_name(), length=length,
                               additiveType=metadata["additiveType"], basePoseType=metadata["basePoseType"],
                               baseAsset=metadata["baseAsset"], baseFrame=metadata["baseFrame"], frames=frames))
    payload = dict(schemaVersion=1, scope="Native RAW individual stop assets, not whole-graph or character parity",
                   evaluation=dict(raw=True, retarget=True, extractRootMotion=False,
                                   incorporateRootMotion=False, retrieveAdditiveAsFullPose=True, evaluateCurves=True), assets=assets)
    (output / "stop-sources.json").write_text(json.dumps(payload, allow_nan=False), encoding="utf-8")
    unreal.log("ALS_FOOT_SOURCE_CONTRACT_OK assets=4 standing_graph=1 assets_saved=0")


export()
if os.environ.get("ALS_FOOT_SOURCE_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

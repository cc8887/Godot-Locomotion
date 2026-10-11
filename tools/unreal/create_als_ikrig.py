"""Create IKRig + IKRetargeter for retargeting Kimodo/Manny clips onto the ALS skeleton.

Run inside GASP58 Editor Python:
    UnrealEditor-Cmd.exe GASP58.uproject -run=pythonscript -script=create_als_ikrig.py

Produces:
  /Game/KimodoMocara/Rigs/IK_ALS_Mannequin        (IKRigDefinition, ALS skeleton, 22 chains)
  /Game/KimodoMocara/Rigs/RTG_Manny_to_ALS        (IKRetargeter, SOURCE=Manny rig, TARGET=ALS rig)

Then dumps rig/retargeter state into KIMODO_ALSRIG_OUT (default under the main repo
assets/generated/kimodo_ue/als_regen/).
"""

import json
import os
import traceback

import unreal

ALS_MESH = os.environ.get("KIMODO_ALS_TARGET_MESH", "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin")
MANNY_RIG = "/Game/Characters/UE5_Mannequins/Rigs/IK_UE5_Mannequin_Retarget"
ALS_RIG_PATH = os.environ.get("KIMODO_ALS_RIG_PATH", "/Game/KimodoMocara/Rigs/IK_ALS_Mannequin")
ALS_RETARGETER_PATH = os.environ.get("KIMODO_RETARGETER_PATH", "/Game/KimodoMocara/Rigs/RTG_Manny_to_ALS")
OUT = os.environ.get(
    "KIMODO_ALSRIG_OUT",
    "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/create_als_ikrig.json",
)

# ALS bone names follow the authoritative v4_recovery 68-bone list (mixed case).
# Chain names mirror IK_UE5_Mannequin_Retarget so auto_map_chains can pair them.
# Metacarpal chains are skipped: ALS skeleton has no metacarpal bones.
CHAINS = [
    # (chain, start, end, goal)
    ("root", "root", "root", "None"),
    ("Spine", "spine_01", "spine_03", "None"),
    ("Neck", "neck_01", "neck_01", "None"),
    ("head", "head", "head", "None"),
    ("LeftLeg", "Thigh_L", "Foot_L", "LeftFootIK"),
    ("LeftClavicle", "clavicle_l", "clavicle_l", "None"),
    ("LeftArm", "UpperArm_L", "Hand_L", "LeftHandIK"),
    ("LeftThumb", "thumb_01_l", "thumb_03_l", "None"),
    ("LeftIndex", "index_01_l", "index_03_l", "None"),
    ("LeftMiddle", "middle_01_l", "middle_03_l", "None"),
    ("LeftRing", "ring_01_l", "ring_03_l", "None"),
    ("LeftPinky", "pinky_01_l", "pinky_03_l", "None"),
    ("RightLeg", "Thigh_R", "Foot_R", "RightFootIK"),
    ("RightClavicle", "clavicle_r", "clavicle_r", "None"),
    ("RightArm", "UpperArm_R", "hand_r", "RightHandIK"),
    ("RightThumb", "thumb_01_r", "thumb_03_r", "None"),
    ("RightIndex", "index_01_r", "index_03_r", "None"),
    ("RightMiddle", "middle_01_r", "middle_03_r", "None"),
    ("RightRing", "ring_01_r", "ring_03_r", "None"),
    ("RightPinky", "pinky_01_r", "pinky_03_r", "None"),
    ("LeftToe", "ball_l", "ball_l", "None"),
    ("RightToe", "ball_r", "ball_r", "None"),
]


def log(msg):
    unreal.log("KIMODO_ALSRIG " + msg)


def main():
    result = {"schemaVersion": 1}

    mesh = unreal.load_asset(ALS_MESH)
    if not isinstance(mesh, unreal.SkeletalMesh):
        raise RuntimeError("ALS mesh not found: " + ALS_MESH)
    als_skeleton = mesh.get_editor_property("skeleton")
    result["alsSkeleton"] = als_skeleton.get_path_name()
    log("als skeleton " + result["alsSkeleton"])

    manny_rig = unreal.load_asset(MANNY_RIG)
    if manny_rig is None:
        raise RuntimeError("Manny rig not found: " + MANNY_RIG)

    if not unreal.EditorAssetLibrary.does_directory_exist("/Game/KimodoMocara/Rigs"):
        unreal.EditorAssetLibrary.make_directory("/Game/KimodoMocara/Rigs")

    # --- IKRig for the ALS skeleton -------------------------------------
    if unreal.EditorAssetLibrary.does_asset_exist(ALS_RIG_PATH):
        als_rig = unreal.load_asset(ALS_RIG_PATH)
        result["rigAction"] = "reused"
    else:
        als_rig = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            ALS_RIG_PATH.rsplit('/',1)[1], ALS_RIG_PATH.rsplit('/',1)[0], unreal.IKRigDefinition, None)
        if als_rig is None:
            raise RuntimeError("create IKRigDefinition failed")
        result["rigAction"] = "created"

    ctrl = unreal.IKRigController.get_controller(als_rig)
    ctrl.set_skeletal_mesh(mesh)
    ctrl.set_retarget_root("Pelvis")
    try:
        ctrl.set_root_motion_bone("Pelvis")
    except Exception as exc:
        log("set_root_motion_bone failed: " + repr(exc))

    # Wipe existing chains first so reruns converge to the same state.
    for ch in ctrl.get_retarget_chains():
        try:
            ctrl.remove_retarget_chain(ch.get_editor_property("chain_name"))
        except Exception:
            pass

    chain_report = []
    for cname, start, end, goal in CHAINS:
        try:
            # goal_name is a required 4th positional arg; "" = no IK goal (FK-only rig).
            ctrl.add_retarget_chain(cname, start, end, "")
            chain_report.append({"chain": cname, "start": start, "end": end, "ok": True})
        except Exception as exc:
            chain_report.append({"chain": cname, "start": start, "end": end,
                                 "ok": False, "error": repr(exc)})
    result["chains"] = chain_report
    failed_chains = [c for c in chain_report if not c["ok"]]
    if failed_chains:
        raise RuntimeError("failed chains: " + json.dumps(failed_chains))

    # Goals for foot/hand IK chains (LeftFootIK etc.) exist only if the rig has
    # solvers/goals; for pure retarget-chain usage they are optional. Record what
    # exists after chain creation.
    goal_report = []
    for cname, start, end, goal in CHAINS:
        if goal == "None":
            continue
        try:
            cur = str(ctrl.get_retarget_chain_goal(cname))
        except Exception as exc:
            cur = "err:" + repr(exc)
        goal_report.append({"chain": cname, "goal": cur})
    result["chainGoals"] = goal_report

    unreal.EditorAssetLibrary.save_loaded_asset(als_rig, only_if_is_dirty=False)
    result["alsRigPath"] = als_rig.get_path_name()
    log("saved rig " + result["alsRigPath"])

    # --- IKRetargeter Manny -> ALS --------------------------------------
    if unreal.EditorAssetLibrary.does_asset_exist(ALS_RETARGETER_PATH):
        rtg = unreal.load_asset(ALS_RETARGETER_PATH)
        result["rtgAction"] = "reused"
    else:
        rtg = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
            ALS_RETARGETER_PATH.rsplit('/',1)[1], ALS_RETARGETER_PATH.rsplit('/',1)[0], unreal.IKRetargeter, None)
        if rtg is None:
            raise RuntimeError("create IKRetargeter failed")
        result["rtgAction"] = "created"

    rc = unreal.IKRetargeterController.get_controller(rtg)
    rc.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE, manny_rig)
    rc.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET, als_rig)
    result["autoMap"] = "exact"
    rc.auto_map_chains(unreal.AutoMapChainType.EXACT, True)
    rc.add_default_ops()

    # --- retarget pose alignment (mirrors Mocara AutoAlignLimbsOnly) ------
    # Without chain-to-chain pose alignment the FK transfer copies source local
    # rotations verbatim; the Manny-vs-ALS bind pose difference (clavicle/upperarm
    # ~24 deg, spine_02 ~10 deg) is then baked into every clip as a constant offset
    # (measured P2: shoulder/hip mean 14-16 deg, arm drop +7 deg).
    # Limbs ONLY: pelvis/spine/neck/head chains point straight up in both skeletons,
    # so chain-to-chain alignment is degenerate there (arbitrary yaw) -- Mocara hit
    # a 90-deg torso yaw when aligning them. Keep torso bones at identity.
    VERTICALLY_DEGENERATE_PREFIXES = ("root", "pelvis", "spine", "neck", "head")
    try:
        # Enumerate limb bones via the ALS rig's own retarget chains: each chain's
        # start/end bones are real ALS bone names, and in-between bones (lowerarm,
        # calf, finger segments) live inside chains already (UpperArm->Hand covers
        # lowerarm; Thigh->Foot covers calf), so chain ends suffice for alignment.
        chain_bones = []
        for ch in ctrl.get_retarget_chains():
            try:
                cname = str(ch.get_editor_property("chain_name"))
                bstart = str(ctrl.get_retarget_chain_start_bone(cname))
                bend = str(ctrl.get_retarget_chain_end_bone(cname))
            except Exception:
                continue
            for b in (bstart, bend):
                if b not in chain_bones:
                    chain_bones.append(b)
        align_bones = [b for b in chain_bones
                       if not b.lower().startswith(VERTICALLY_DEGENERATE_PREFIXES)]
        result["alignBones"] = align_bones
        result["alignBonesSource"] = "retarget_chain_ends"
        if align_bones:
            pose_name = rc.get_current_retarget_pose_name(unreal.RetargetSourceOrTarget.TARGET)
            # clear any stale offsets from a previous run first (Mocara lesson:
            # un-reset stale values survive and silently defeat the fix).
            rc.reset_retarget_pose(
                pose_name, [],
                unreal.RetargetSourceOrTarget.TARGET)
            rc.auto_align_bones(
                align_bones,
                unreal.RetargetAutoAlignMethod.CHAIN_TO_CHAIN,
                unreal.RetargetSourceOrTarget.TARGET)
            result["retargetPoseAligned"] = True
            log("auto-aligned %d limb bones (torso at identity)" % len(align_bones))
        else:
            result["retargetPoseAligned"] = False
            result["retargetPoseAlignError"] = "no bones resolved"
    except Exception as exc:
        result["retargetPoseAligned"] = False
        result["retargetPoseAlignError"] = repr(exc)
        log("retarget pose align failed: " + repr(exc))
    result["chainSettings"] = []
    unmapped = []
    try:
        for s in rc.get_all_chain_settings():
            tgt = str(s.get_editor_property("target_chain"))
            src = str(s.get_editor_property("source_chain"))
            result["chainSettings"].append({"chain": tgt, "source": src})
            if src == "None":
                unmapped.append(tgt)
        result["unmappedChains"] = unmapped
    except Exception as exc:
        result["chainSettingsError"] = repr(exc)
    result["ops"] = []
    try:
        result["ops"] = [str(o) for o in rc.get_retarget_ops()]
    except Exception as exc:
        result["opsProbeError"] = repr(exc)

    unreal.EditorAssetLibrary.save_loaded_asset(rtg, only_if_is_dirty=False)
    result["retargeterPath"] = rtg.get_path_name()
    log("saved retargeter " + result["retargeterPath"])

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=1)
    log("DONE " + OUT)
    print("KIMODO_ALSRIG_DONE " + OUT)


try:
    main()
except Exception:
    print("KIMODO_ALSRIG_FAIL " + traceback.format_exc())
    unreal.log_error("KIMODO_ALSRIG_FAIL")
    raise

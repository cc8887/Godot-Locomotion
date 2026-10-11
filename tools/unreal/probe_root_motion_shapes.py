# 三层资产 root motion 形态对比探针:
#   orig  = 原版 ALS AnimationExamples (形态基准, 判定原版 root/flag 语义)
#   regen = Kimodo 一段重定向 ALS_Regen (Manny 骨架, 普通版, 位移应在 pelvis)
#   rootik= Manny 阶段根运动烘焙 ALS_Regen_RootIK (烘焙直出, 判定烘焙是否真写 root 轨道)
#   final = ALS_Final 二段重定向产物 (判定 root 轨迹是否被二段重定向剥回 pelvis)
# 每条记录 rootMotionEnabled + root/pelvis/ik_foot_l/ik_hand_l WORLD 采样与位移幅度
# API 依据: audit_motion_shape.py 已验证 AnimPose 模板 (AnimPoseExtensions + 直接属性赋值)
import json
import os

import unreal

FBX_DIR = "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/fbx_als"
OUT = os.path.join(FBX_DIR, "root_motion_shapes.json")

ROOTIK_CLIPS = [
    "ALS_N_FallLoop_Fast", "ALS_N_FallLoop",
    "ALS_N_Falling_LeanPose_0", "ALS_N_Falling_LeanPose_B", "ALS_N_Falling_LeanPose_F",
    "ALS_N_Falling_LeanPose_L", "ALS_N_Falling_LeanPose_R",
    "ALS_N_JumpLoop", "ALS_N_JumpRun_LF", "ALS_N_JumpRun_RF",
    "ALS_N_JumpWalk_LF", "ALS_N_JumpWalk_RF",
    "ALS_N_LandRoll_F", "ALS_N_Land_Heavy", "ALS_N_Land_Light",
    "ALS_N_Mantle_1m_LH", "ALS_N_Mantle_1m_RH", "ALS_N_Mantle_2m",
    "ALS_N_Transition_L", "ALS_N_Transition_R",
]

LAYER_DIRS = {
    "orig": "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples",
    "regen": "/Game/KimodoMocara/ALS_Regen",
    "rootik": "/Game/KimodoMocara/ALS_Regen_RootIK",
    "final": "/Game/KimodoMocara/ALS_Final",
}

BONES = ["root", "pelvis", "ik_foot_l", "ik_hand_l"]
FRAMES = 6


def index_dir(base):
    out = {}
    for entry in unreal.EditorAssetLibrary.list_assets(base, recursive=True):
        path = str(entry).split(".")[0]
        name = path.rsplit("/", 1)[-1]
        out.setdefault(name, path)
    return out


def sample(anim):
    duration = float(anim.get_play_length())
    options = unreal.AnimPoseEvaluationOptions()
    options.evaluation_type = unreal.AnimDataEvalType.RAW
    options.should_retarget = True
    options.extract_root_motion = False
    options.incorporate_root_motion_into_pose = False
    options.retrieve_additive_as_full_pose = True
    options.evaluate_curves = False
    rows = []
    for i in range(FRAMES + 1):
        t = duration * i / FRAMES
        pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(anim, t, options)
        row = {"t": round(t, 3)}
        for b in BONES:
            tr = unreal.AnimPoseExtensions.get_bone_pose(pose, b, unreal.AnimPoseSpaces.WORLD)
            p = tr.translation
            row[b] = [round(p.x, 2), round(p.y, 2), round(p.z, 2)]
        rows.append(row)
    spread = {}
    for b in BONES:
        spread[b] = [round(max(r[b][k] for r in rows) - min(r[b][k] for r in rows), 2) for k in range(3)]
    return {
        "rootMotion": bool(unreal.AnimationLibrary.is_root_motion_enabled(anim)),
        "duration": round(duration, 3),
        "spread": spread,
        "first": rows[0],
        "last": rows[-1],
    }


def main():
    indexes = {layer: index_dir(base) for layer, base in LAYER_DIRS.items()}
    print("INDEXED " + " ".join(layer + "=" + str(len(idx)) for layer, idx in indexes.items()))
    results = {}
    missing = []
    for clip in ROOTIK_CLIPS:
        entry = {}
        wanted = [
            ("orig", clip),
            ("regen", "AS_" + clip),
            ("rootik", "AS_" + clip + "_RootIK"),
            ("final", "AS_" + clip),
        ]
        for layer, asset_name in wanted:
            path = indexes[layer].get(asset_name)
            if path is None:
                missing.append(layer + ":" + asset_name)
                entry[layer] = {"missing": True}
                continue
            anim = unreal.load_asset(path)
            if anim is None:
                missing.append(layer + ":load:" + path)
                entry[layer] = {"missing": True, "path": path}
                continue
            try:
                entry[layer] = sample(anim)
                entry[layer]["path"] = path
            except Exception as e:
                entry[layer] = {"error": repr(e)[:200], "path": path}
        results[clip] = entry
        orig_rm = entry.get("orig", {}).get("rootMotion")
        rootik_rm = entry.get("rootik", {}).get("rootMotion")
        final_rm = entry.get("final", {}).get("rootMotion")
        rootik_root_spread = entry.get("rootik", {}).get("spread", {}).get("root")
        final_root_spread = entry.get("final", {}).get("spread", {}).get("root")
        orig_root_spread = entry.get("orig", {}).get("spread", {}).get("root")
        print("PROBED %s orig_rm=%s orig_root=%s rootik_rm=%s rootik_root=%s final_rm=%s final_root=%s" % (
            clip, orig_rm, orig_root_spread, rootik_rm, rootik_root_spread, final_rm, final_root_spread))
    with open(OUT, "w") as f:
        json.dump({"clips": results, "missing": missing}, f, indent=1)
    print("KIMODO_ROOT_MOTION_PROBE_DONE clips=%d missing=%d out=%s" % (len(results), len(missing), OUT))


main()

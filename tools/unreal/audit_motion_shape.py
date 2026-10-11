# 抽查重定向输出运动形态: 在 UE commandlet 中求值重定向后动画的关键骨轨迹
# 采样 pelvis/root 的世界位移与旋转, 区分 Walk(有位移)/Pose(静止)/FallLoop(root 轨道)
# 用途: 替代无法完成的 chainSettings 映射审计, 产物级确认重定向语义正确
# API 依据: probe_animpose_api.py 实测 + export_foot_lock_source_contract.py 已验证模式
#   - AnimPoseExtensions.get_anim_pose_at_time(anim, time, options) -> AnimPose
#   - AnimPoseExtensions.get_bone_pose(pose, bone, unreal.AnimPoseSpaces.WORLD) -> FTransform
#   - AnimPoseEvaluationOptions 直接属性赋值(无 b_ 前缀, 无 world_space 属性)
# 注意: ALS_Final 下 InAir/Transitions 20 条为烘焙版(root 骨轨道携带位移, rootMotion=true)
import json
import os

import unreal

FBX_DIR = "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/fbx_als"
OUT = os.path.join(FBX_DIR, "motion_shape_audit.json")

SAMPLES = [
    # (clip, assetPath, 期望)
    ("ALS_N_Walk_F", "/Game/KimodoMocara/ALS_Final/AS_ALS_N_Walk_F", "walk_pelvis_moves"),
    ("ALS_N_FallLoop", "/Game/KimodoMocara/ALS_Final/AS_ALS_N_FallLoop", "rootik_root_moves"),
    ("ALS_N_JumpRun_LF", "/Game/KimodoMocara/ALS_Final/AS_ALS_N_JumpRun_LF", "rootik_root_moves"),
    ("ALS_CLF_Pose", "/Game/KimodoMocara/ALS_Final/AS_ALS_CLF_Pose", "pose_static"),
    ("ALS_N_Land_Light", "/Game/KimodoMocara/ALS_Final/AS_ALS_N_Land_Light", "rootik_root_moves"),
]

BONES = ["pelvis", "root"]


def main():
    results = []
    for clip, path, expect in SAMPLES:
        try:
            anim = unreal.load_asset(path)
            if anim is None:
                results.append({"clip": clip, "error": "load failed"})
                continue
            seq = anim.get_play_length()
            root_motion = unreal.AnimationLibrary.is_root_motion_enabled(anim)
            duration = float(seq)
            frames = min(10, max(2, int(duration * 10)))
            options = unreal.AnimPoseEvaluationOptions()
            options.evaluation_type = unreal.AnimDataEvalType.RAW
            options.should_retarget = True
            options.extract_root_motion = False
            options.incorporate_root_motion_into_pose = False
            options.retrieve_additive_as_full_pose = True
            options.evaluate_curves = True
            samples = []
            for i in range(frames + 1):
                t = duration * i / frames
                pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(anim, t, options)
                frame_data = {"t": round(t, 4)}
                for b in BONES:
                    tr = unreal.AnimPoseExtensions.get_bone_pose(pose, b, unreal.AnimPoseSpaces.WORLD)
                    frame_data[b + ".x"] = round(tr.translation.x, 4)
                    frame_data[b + ".y"] = round(tr.translation.y, 4)
                    frame_data[b + ".z"] = round(tr.translation.z, 4)
                    frame_data[b + ".rot_z_deg"] = round(tr.rotation.rotator().roll, 4)
                samples.append(frame_data)
            results.append({
                "clip": clip,
                "expect": expect,
                "rootMotionEnabled": bool(root_motion),
                "duration": round(duration, 4),
                "samples": samples,
            })
            print("SAMPLED %s expect=%s rm=%s dur=%.2f frames=%d" % (clip, expect, root_motion, duration, frames))
        except Exception as e:
            results.append({"clip": clip, "error": repr(e)[:300]})
            print("ERROR %s %r" % (clip, e))

    with open(OUT, "w") as f:
        json.dump(results, f, indent=2)
    print("KIMODO_MOTION_AUDIT_DONE samples=%d out=%s" % (len(results), OUT))


main()

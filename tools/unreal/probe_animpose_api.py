# 探测 UE 5.8 Python 侧 AnimPose 真实 API
import unreal

print("PROBE start")
print("HAS_AnimPose", hasattr(unreal, "AnimPose"))
print("HAS_AnimPoseEvaluationOptions", hasattr(unreal, "AnimPoseEvaluationOptions"))
print("HAS_AnimPoseExtensions", hasattr(unreal, "AnimPoseExtensions"))
print("HAS_AnimPoseSpaces", hasattr(unreal, "AnimPoseSpaces"))

if hasattr(unreal, "AnimPoseExtensions"):
    methods = [m for m in dir(unreal.AnimPoseExtensions) if not m.startswith("_")]
    print("EXT_METHODS", methods)

if hasattr(unreal, "AnimPoseEvaluationOptions"):
    opts = unreal.AnimPoseEvaluationOptions()
    try:
        props = [p for p in dir(opts) if not p.startswith("_")]
        print("OPT_PROPS", props)
    except Exception as e:
        print("OPT_PROPS_ERR", repr(e))

if hasattr(unreal, "AnimPoseSpaces"):
    print("SPACES", [s for s in dir(unreal.AnimPoseSpaces) if not s.startswith("_")])

if hasattr(unreal, "AnimPoseExtensions"):
    for name in ("get_anim_pose_at_time", "get_bone_pose", "get_curve_names", "get_curve_weight"):
        print("HAS_%s" % name, hasattr(unreal.AnimPoseExtensions, name))

anim = unreal.load_asset("/Game/KimodoMocara/ALS_Final/AS_ALS_N_Walk_F")
print("ANIM_LOADED", anim is not None)
if anim is not None and hasattr(unreal, "AnimPoseExtensions") and hasattr(unreal.AnimPoseExtensions, "get_anim_pose_at_time"):
    try:
        options = unreal.AnimPoseEvaluationOptions()
        options.set_editor_property("b_evaluate_curves", True)
        pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(anim, 0.0, options)
        print("EVAL_OK type=%s" % type(pose).__name__)
        tr = unreal.AnimPoseExtensions.get_bone_pose(pose, "pelvis", unreal.AnimPoseSpaces.WORLD)
        print("PELVIS x=%.2f y=%.2f z=%.2f" % (tr.translation.x, tr.translation.y, tr.translation.z))
        curves = []
        unreal.AnimPoseExtensions.get_curve_names(pose, curves)
        print("CURVES", sorted(str(c) for c in curves))
    except Exception as e:
        print("EVAL_ERR", repr(e))

print("PROBE done")

import json
import os

import unreal

out = os.environ.get("KIMODO_PROBE_OUT", "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/probe_batch_api.json")
report = {"steps": []}

def step(name, fn):
    try:
        val = fn()
        report["steps"].append({"name": name, "ok": True, "value": str(val)[:400]})
    except Exception as e:
        report["steps"].append({"name": name, "ok": False, "error": repr(e)[:400]})

# 1. IKRetargetBatchOperationInputs 类是否存在
def s1():
    return type(unreal.IKRetargetBatchOperationInputs())
step("class IKRetargetBatchOperationInputs", s1)

# 2. 构造实例
def s2():
    obj = unreal.IKRetargetBatchOperationInputs()
    return obj
step("construct inputs", s2)

# 3. 字段探测（列举可用 editor property）
def s3():
    obj = unreal.IKRetargetBatchOperationInputs()
    return [str(p) for p in dir(obj) if not p.startswith("_")][:80]
step("dir(inputs)", s3)

# 4. assets_to_retarget 可赋值
def s4():
    obj = unreal.IKRetargetBatchOperationInputs()
    obj.assets_to_retarget = []
    return "set ok"
step("set assets_to_retarget", s4)

# 5. b_overwrite_existing_files 可赋值
def s5():
    obj = unreal.IKRetargetBatchOperationInputs()
    obj.b_overwrite_existing_files = True
    return "set ok"
step("set b_overwrite_existing_files", s5)

# 6. IKRetargetBatchOperation 静态方法
def s6():
    return unreal.IKRetargetBatchOperation.run_batch_retarget
step("static run_batch_retarget", s6)

# 7. ALS 骨架 bone names API 探测
def s7():
    skel = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton")
    if skel is None:
        return "skeleton load failed"
    rs = skel.get_editor_property("reference_skeleton")
    return [str(m) for m in dir(rs) if not m.startswith("_")][:60]
step("reference_skeleton dir", s7)

# 8. AnimSequence track_names 读取
def s8():
    anim = unreal.load_asset("/Game/KimodoMocara/ALS_Regen/AS_ALS_N_Walk_F")
    if anim is None:
        return "anim load failed"
    return [str(t) for t in anim.get_editor_property("track_names") if t][:20]
step("ALS_Regen AS_ALS_N_Walk_F track_names", s8)

# 9. AnimationLibrary.is_root_motion_enabled
def s9():
    anim = unreal.load_asset("/Game/KimodoMocara/ALS_Regen/AS_ALS_N_Walk_F")
    return unreal.AnimationLibrary.is_root_motion_enabled(anim)
step("is_root_motion_enabled", s9)

with open(out, "w") as f:
    json.dump(report, f, indent=2)
print("KIMODO_PROBE_BATCH_DONE " + out)

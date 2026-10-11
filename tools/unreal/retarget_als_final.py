# 二段重定向单条：Manny 骨架 AnimSequence -> ALS_Mannequin_Skeleton（经 RTG_Manny_to_ALS）
# env: KIMODO_INPUT_ASSET_PATH(源资产), KIMODO_ASSET_PATH(期望输出资产路径,用于exists跳过与校验),
#      KIMODO_FBX_OUT(磁盘FBX), KIMODO_SEARCH/KIMODO_REPLACE(可选名称替换,RootIK去后缀用)
import gc
import hashlib
import json
import os

import unreal

ENV_KEYS = [
    "KIMODO_INPUT_ASSET_PATH",
    "KIMODO_ASSET_PATH",
    "KIMODO_FBX_OUT",
    "KIMODO_SEARCH",
    "KIMODO_REPLACE",
]
RETARGETER = os.environ.get("KIMODO_RETARGETER_PATH", "/Game/KimodoMocara/Rigs/RTG_Manny_to_ALS.RTG_Manny_to_ALS")
ALS_MESH = os.environ.get("KIMODO_ALS_TARGET_MESH", "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin")
MANNY_MESH = "/Game/Characters/UE5_Mannequins/Meshes/SKM_Manny.SKM_Manny"
ALS_SKELETON_NAME = os.environ.get("KIMODO_ALS_SKELETON_NAME", "ALS_Mannequin_Skeleton")
V4_RECOVERY = "F:/Github/Godot-Locomotion/assets/config/v4_recovery_movement_source_inputs.json"
TARGET_DIR = os.environ.get("KIMODO_ALS_TARGET_DIR", "/Game/KimodoMocara/ALS_Final")


def die(msg):
    print("KIMODO_ALS_FINAL_FAIL " + msg)
    raise RuntimeError(msg)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def find_key(obj, key):
    if isinstance(obj, dict):
        if key in obj:
            return obj[key]
        for v in obj.values():
            r = find_key(v, key)
            if r is not None:
                return r
    elif isinstance(obj, list):
        for v in obj:
            r = find_key(v, key)
            if r is not None:
                return r
    return None


def load_als_bone_set():
    with open(V4_RECOVERY, "r") as f:
        data = json.load(f)
    names = find_key(data, "rawBoneNames")
    if not names:
        die("rawBoneNames not found in v4_recovery json")
    return set(str(n).casefold() for n in names), len(names)


def get_pose_bone_names(anim):
    options = unreal.AnimPoseEvaluationOptions()
    pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(anim, 0.0, options)
    try:
        bones = pose.get_bone_names()
    except AttributeError:
        bones = unreal.AnimPoseExtensions.get_bone_names(pose)
    return [str(b) for b in bones]


def main():
    env = os.environ
    for k in ["KIMODO_INPUT_ASSET_PATH", "KIMODO_ASSET_PATH", "KIMODO_FBX_OUT"]:
        if not env.get(k):
            die("missing env " + k)
    in_path = env["KIMODO_INPUT_ASSET_PATH"]
    out_path = env["KIMODO_ASSET_PATH"]
    fbx_out = env["KIMODO_FBX_OUT"]
    search = env.get("KIMODO_SEARCH", "")
    replace = env.get("KIMODO_REPLACE", "")

    # 续跑跳过: FBX + manifest 均存在
    if os.path.exists(fbx_out) and os.path.exists(fbx_out + ".source.json"):
        print("KIMODO_ALS_FINAL_SKIP " + out_path)
        return "skip"

    src_anim = unreal.load_asset(in_path)
    if src_anim is None:
        die("source anim not found: " + in_path)
    src_root_motion = bool(unreal.AnimationLibrary.is_root_motion_enabled(src_anim))

    retargeter = unreal.load_asset(RETARGETER)
    if retargeter is None:
        die("retargeter not found: " + RETARGETER)

    inputs = unreal.IKRetargetBatchOperationInputs()
    asset_data = unreal.EditorAssetLibrary.find_asset_data(in_path)
    if not asset_data:
        die("find_asset_data failed: " + in_path)
    inputs.assets_to_retarget = [asset_data]
    inputs.source_mesh = unreal.load_asset(MANNY_MESH)
    inputs.target_mesh = unreal.load_asset(ALS_MESH)
    inputs.ik_retarget_asset = retargeter
    inputs.override_set_names = []
    inputs.search = search
    inputs.replace = replace
    inputs.prefix = ""
    inputs.suffix = ""
    inputs.target_path = TARGET_DIR
    inputs.use_source_path = False
    inputs.include_referenced_assets = False
    inputs.overwrite_existing_files = True
    inputs.retain_additive_flags = True

    results = unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)
    if results is None or len(results) == 0:
        die("batch retarget returned no assets")
    out_data = results[0]
    # AssetData 是 UStruct, .object_path 直接访问抛 AttributeError; 三级回退解析
    anim = None
    try:
        anim = unreal.AssetRegistryHelpers.get_asset(out_data)
    except Exception as e:
        print("KIMODO_ALS_FINAL getAssetProbeError " + repr(e)[:200])
    if anim is None:
        pkg = ""
        try:
            pkg = str(out_data.get_editor_property("package_name"))
        except Exception as e:
            print("KIMODO_ALS_FINAL pkgProbeError " + repr(e)[:200])
        if pkg and pkg != "None":
            anim = unreal.load_asset(pkg)
    if anim is None:
        anim = unreal.load_asset(out_path)
    if anim is None:
        die("could not resolve retarget output asset from AssetData")
    out_obj_path = anim.get_path_name()
    print("KIMODO_ALS_FINAL output " + out_obj_path)

    # 硬校验: 输出绑定 ALS 骨架
    skel = anim.get_editor_property("skeleton")
    if ALS_SKELETON_NAME not in str(skel.get_path_name()):
        die("output skeleton is not ALS: " + str(skel.get_path_name()))

    # root motion 状态对齐源
    out_root_motion = bool(unreal.AnimationLibrary.is_root_motion_enabled(anim))
    if out_root_motion != src_root_motion:
        unreal.AnimationLibrary.set_root_motion_enabled(anim, src_root_motion)

    # 软审计: 骨名集合 vs ALS 68 骨权威清单
    als_set, als_count = load_als_bone_set()
    pose_bones = []
    try:
        pose_bones = get_pose_bone_names(anim)
    except Exception as e:
        pose_bones = []
        print("KIMODO_ALS_FINAL boneNameProbeError " + repr(e)[:200])
    unknown = sorted(set(b for b in pose_bones if b.casefold() not in als_set))

    # 保存(对齐模板: 强制保存并校验)
    if not unreal.EditorAssetLibrary.save_loaded_asset(anim, only_if_is_dirty=False):
        die("failed to save retargeted asset: " + out_obj_path)

    # FBX 导出(统一选项, 对齐已验证一段重定向模板)
    os.makedirs(os.path.dirname(fbx_out), exist_ok=True)
    opts = unreal.FbxExportOption()
    opts.set_editor_property("fbx_export_compatibility", unreal.FbxExportCompatibility.FBX_2020)
    opts.set_editor_property("ascii", True)
    opts.set_editor_property("force_front_x_axis", False)
    opts.set_editor_property("map_skeletal_motion_to_root", False)
    task = unreal.AssetExportTask()
    task.set_editor_property("object", anim)
    task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
    task.set_editor_property("filename", fbx_out)
    task.set_editor_property("options", opts)
    task.set_editor_property("automated", True)
    task.set_editor_property("prompt", False)
    task.set_editor_property("replace_identical", False)
    task.set_editor_property("use_file_archive", True)
    if not unreal.Exporter.run_asset_export_task(task) or not os.path.isfile(fbx_out):
        die("fbx export failed: " + fbx_out + " errors=" + str(task.get_editor_property("errors")))

    manifest = {
        "schemaVersion": 1,
        "pipeline": "kimodo-als-final",
        "sourceAnimPath": in_path,
        "retargetedAssetPath": out_obj_path,
        "retargeter": RETARGETER,
        "targetSkeleton": ALS_SKELETON_NAME,
        "fbxPath": fbx_out,
        "fbxSha256": sha256_file(fbx_out),
        "sourceRootMotionEnabled": src_root_motion,
        "outputRootMotionEnabled": src_root_motion,
        "alsBoneCount": als_count,
        "poseBoneCount": len(pose_bones),
        "unknownBones": unknown,
    }
    with open(fbx_out + ".source.json", "w") as f:
        json.dump(manifest, f, indent=2)

    print("KIMODO_ALS_FINAL_DONE " + out_path + " unknown=" + str(len(unknown)))
    return "done"


try:
    main()
finally:
    for k in ENV_KEYS:
        os.environ.pop(k, None)
    globals().pop("main", None)
    globals().pop("die", None)
    globals().pop("sha256_file", None)
    globals().pop("find_key", None)
    globals().pop("load_als_bone_set", None)
    globals().pop("get_pose_bone_names", None)
    gc.collect()

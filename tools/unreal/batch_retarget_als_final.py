# 二段批量重定向驱动: runpy 逐条模式 (与既有 batch_retarget_als_regen.py 一致)
# 源清单 = ALS_Regen 123 条中排除 20 条 InAir/Transitions 后的 103 条普通版
#         + ALS_Regen_RootIK 20 条烘焙版 (search/replace 去 _RootIK 后缀)
# 输出: /Game/KimodoMocara/ALS_Final + als_regen/fbx_als/
import json
import os
import sys

import unreal

REGEN_ROOT = "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen"
REPORT = os.path.join(REGEN_ROOT, "als_final_batch_report.json")
ASSET_DIR = "/Game/KimodoMocara/ALS_Regen"
ROOTIK_DIR = "/Game/KimodoMocara/ALS_Regen_RootIK"
FBX_DIR = os.path.join(REGEN_ROOT, "fbx_als")
SINGLE = "F:/Github/Godot-Locomotion/tools/unreal/retarget_als_final.py"

# 烘焙版名单 (与 batch_bake_root_ik.py CLIPS 对应, 已去 AS_ 前缀)
ROOTIK_CLIPS = [
    "ALS_N_FallLoop_Fast", "ALS_N_FallLoop", "ALS_N_Falling_LeanPose_0",
    "ALS_N_Falling_LeanPose_B", "ALS_N_Falling_LeanPose_F",
    "ALS_N_Falling_LeanPose_L", "ALS_N_Falling_LeanPose_R",
    "ALS_N_JumpLoop", "ALS_N_JumpRun_LF", "ALS_N_JumpRun_RF",
    "ALS_N_JumpWalk_LF", "ALS_N_JumpWalk_RF", "ALS_N_LandRoll_F",
    "ALS_N_Land_Heavy", "ALS_N_Land_Light",
    "ALS_N_Mantle_1m_LH", "ALS_N_Mantle_1m_RH", "ALS_N_Mantle_2m",
    "ALS_N_Transition_L", "ALS_N_Transition_R",
]

SINGLE_KEYS = ["KIMODO_INPUT_ASSET_PATH", "KIMODO_ASSET_PATH", "KIMODO_FBX_OUT",
               "KIMODO_SEARCH", "KIMODO_REPLACE"]


def build_jobs():
    jobs = []
    # 普通版: ALS_Regen 全部 AS_* 资产, 排除烘焙名单
    regen_assets = unreal.EditorAssetLibrary.list_assets(ASSET_DIR, recursive=False)
    rootik_set = set(ROOTIK_CLIPS)
    normal = []
    for a in regen_assets:
        name = a.split("/")[-1].split(".")[0]
        if not name.startswith("AS_"):
            continue
        clip = name[3:]
        if clip in rootik_set:
            continue
        normal.append((name, clip))
    # 烘焙版
    rootik_assets = unreal.EditorAssetLibrary.list_assets(ROOTIK_DIR, recursive=False)
    baked = []
    for a in rootik_assets:
        name = a.split("/")[-1].split(".")[0]
        if not name.startswith("AS_"):
            continue
        clip = name[3:]
        if not clip.endswith("_RootIK"):
            continue
        baked.append((name, clip))
    for name, clip in normal:
        jobs.append({
            "kind": "normal",
            "input": ASSET_DIR + "/" + name,
            "outAsset": TARGET_NAME(clip),
            "fbx": os.path.join(FBX_DIR, clip + ".fbx"),
            "search": "", "replace": "",
        })
    for name, clip in baked:
        base = clip[:-len("_RootIK")]
        jobs.append({
            "kind": "rootik",
            "input": ROOTIK_DIR + "/" + name,
            "outAsset": TARGET_NAME(base),
            "fbx": os.path.join(FBX_DIR, base + ".fbx"),
            "search": "_RootIK", "replace": "",
        })
    return jobs


def TARGET_NAME(clip):
    return "/Game/KimodoMocara/ALS_Final/AS_" + clip


def main():
    import runpy
    os.makedirs(FBX_DIR, exist_ok=True)

    jobs = build_jobs()
    report = {"total": len(jobs), "ok": 0, "failed": 0, "skipped": 0, "results": []}
    print("KIMODO_ALS_FINAL_BATCH_START total=%d" % len(jobs))

    for i, job in enumerate(jobs):
        clip = os.path.splitext(os.path.basename(job["fbx"]))[0]
        # exists 跳过
        if os.path.exists(job["fbx"]) and os.path.exists(job["fbx"] + ".source.json"):
            report["skipped"] += 1
            report["results"].append({"clip": clip, "status": "skip"})
            continue
        env = os.environ.copy()
        env["KIMODO_INPUT_ASSET_PATH"] = job["input"]
        env["KIMODO_ASSET_PATH"] = job["outAsset"]
        env["KIMODO_FBX_OUT"] = job["fbx"]
        env["KIMODO_SEARCH"] = job["search"]
        env["KIMODO_REPLACE"] = job["replace"]
        for k in SINGLE_KEYS:
            os.environ.pop(k, None)
        os.environ.update({
            "KIMODO_INPUT_ASSET_PATH": job["input"],
            "KIMODO_ASSET_PATH": job["outAsset"],
            "KIMODO_FBX_OUT": job["fbx"],
            "KIMODO_SEARCH": job["search"],
            "KIMODO_REPLACE": job["replace"],
        })
        try:
            runpy.run_path(SINGLE, run_name="__main__")
            status = "ok"
            report["ok"] += 1
        except SystemExit as e:
            if e.code == 0:
                status = "ok"
                report["ok"] += 1
            else:
                status = "failed"
                report["failed"] += 1
        except Exception as e:
            status = "failed"
            report["failed"] += 1
            report["results"].append({"clip": clip, "status": status, "error": repr(e)[:300]})
            continue
        report["results"].append({"clip": clip, "status": status})

    with open(REPORT, "w") as f:
        json.dump(report, f, indent=2)
    print("KIMODO_ALS_FINAL_BATCH_DONE total=%d ok=%d failed=%d skipped=%d report=%s"
          % (report["total"], report["ok"], report["failed"], report["skipped"], REPORT))
    if report["failed"] > 0:
        sys.exit(1)


main()

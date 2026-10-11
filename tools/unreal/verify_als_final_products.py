import json
import os
import sys

FBX_DIR = r"F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/fbx_als"
REPORT = r"F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/als_final_batch_report.json"

ROOTIK_CLIPS = {
    "ALS_N_FallLoop_Fast", "ALS_N_FallLoop", "ALS_N_Falling_LeanPose_0",
    "ALS_N_Falling_LeanPose_B", "ALS_N_Falling_LeanPose_F",
    "ALS_N_Falling_LeanPose_L", "ALS_N_Falling_LeanPose_R",
    "ALS_N_JumpLoop", "ALS_N_JumpRun_LF", "ALS_N_JumpRun_RF",
    "ALS_N_JumpWalk_LF", "ALS_N_JumpWalk_RF", "ALS_N_LandRoll_F",
    "ALS_N_Land_Heavy", "ALS_N_Land_Light",
    "ALS_N_Mantle_1m_LH", "ALS_N_Mantle_1m_RH", "ALS_N_Mantle_2m",
    "ALS_N_Transition_L", "ALS_N_Transition_R",
}


def main():
    problems = []

    # 1) report 检查
    with open(REPORT, "r", encoding="utf-8") as f:
        report = json.load(f)
    if report.get("total") != 123 or report.get("ok") != 122 or report.get("failed") != 0 or report.get("skipped") != 1:
        problems.append("report stats mismatch: %s" % {k: report.get(k) for k in ("total", "ok", "failed", "skipped")})
    clips = [r["clip"] for r in report["results"]]
    polluted = [c for c in clips if "." in c]
    if polluted:
        problems.append("report clip names still polluted: %d e.g. %s" % (len(polluted), polluted[:3]))
    skip_clips = [r["clip"] for r in report["results"] if r["status"] == "skip"]
    if skip_clips != ["ALS_N_Walk_F"]:
        problems.append("unexpected skip clips: %s" % skip_clips)

    # 2) FBX 产物检查
    fbxs = sorted(f for f in os.listdir(FBX_DIR) if f.endswith(".fbx"))
    manifests = sorted(f for f in os.listdir(FBX_DIR) if f.endswith(".fbx.source.json"))
    if len(fbxs) != 123:
        problems.append("fbx count=%d expect 123" % len(fbxs))
    if len(manifests) != 123:
        problems.append("manifest count=%d expect 123" % len(manifests))
    bad_names = [f for f in fbxs if ".AS_" in f]
    if bad_names:
        problems.append("polluted fbx names remain: %d e.g. %s" % (len(bad_names), bad_names[:3]))
    fbx_clips = set(os.path.splitext(f)[0] for f in fbxs)
    if fbx_clips != set(c for c in clips):
        problems.append("fbx clips != report clips (diff: %s)" % (fbx_clips ^ set(clips)))

    # 3) manifest 抽查: 烘焙版 vs 普通版
    rootik_ok = 0
    for clip in sorted(ROOTIK_CLIPS):
        mp = os.path.join(FBX_DIR, clip + ".fbx.source.json")
        if not os.path.isfile(mp):
            problems.append("rootik manifest missing: %s" % clip)
            continue
        with open(mp, "r", encoding="utf-8") as f:
            m = json.load(f)
        if m.get("sourceAnimPath", "").startswith("/Game/KimodoMocara/ALS_Regen_RootIK/"):
            if m.get("sourceRootMotionEnabled") is True and m.get("outputRootMotionEnabled") is True:
                rootik_ok += 1
            else:
                problems.append("%s rootMotion flags wrong: src=%s out=%s" % (clip, m.get("sourceRootMotionEnabled"), m.get("outputRootMotionEnabled")))
        else:
            problems.append("%s sourceAnimPath wrong: %s" % (clip, m.get("sourceAnimPath")))
    print("ROOTIK manifests ok: %d/20" % rootik_ok)

    # 普通版抽查 3 条: Walk_F(smoke), Walk_B, CLF_GetUp_Back
    for clip in ("ALS_N_Walk_F", "ALS_N_Walk_B", "ALS_CLF_GetUp_Back"):
        mp = os.path.join(FBX_DIR, clip + ".fbx.source.json")
        if not os.path.isfile(mp):
            problems.append("normal manifest missing: %s" % clip)
            continue
        with open(mp, "r", encoding="utf-8") as f:
            m = json.load(f)
        if not m.get("sourceAnimPath", "").startswith("/Game/KimodoMocara/ALS_Regen/"):
            problems.append("%s sourceAnimPath wrong: %s" % (clip, m.get("sourceAnimPath")))
        if m.get("outputRootMotionEnabled") is not False:
            problems.append("%s outputRootMotionEnabled expect False got %s" % (clip, m.get("outputRootMotionEnabled")))

    # 4) 全量 manifest 骨计数快速审计 (unknownBones 是 VB 虚拟骨名字列表)
    bad_pose = []
    for mf in manifests:
        with open(os.path.join(FBX_DIR, mf), "r", encoding="utf-8") as f:
            m = json.load(f)
        unknown = m.get("unknownBones")
        unknown_ok = isinstance(unknown, list) and all(str(b).startswith("VB ") for b in unknown)
        if m.get("alsBoneCount") != 68 or m.get("poseBoneCount") != 79 or not unknown_ok:
            bad_pose.append(os.path.splitext(mf)[0])
    if bad_pose:
        problems.append("bone count anomalies: %d e.g. %s" % (len(bad_pose), bad_pose[:5]))

    if problems:
        print("VERIFY FAILED:")
        for p in problems:
            print(" -", p)
        return 1
    print("VERIFY OK: report=123/122/0/1, fbx=123 clean names, rootik 20 manifests source+rootMotion true, normal manifests false, bone counts 68/79/11 uniform")
    return 0


if __name__ == "__main__":
    sys.exit(main())

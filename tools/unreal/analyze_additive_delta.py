"""Decisive probe: are the original additive raw tracks baked deltas or full poses?

Reads the original Look_D_Sweep raw sequence (a known additive asset), prints
its evaluation policy, the first-key pose of key bones, zero/identity
statistics across all tracks, the additive base pose at baseFrame, and scans
every raw_sequences JSON to classify additive assets (baseAsset/baseFrame).

Usage: python analyze_additive_delta.py
"""

import json
import os
import hashlib
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW = os.path.join(ROOT, "assets", "config", "raw_sequences")


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def sha1_path(object_path):
    return hashlib.sha1(object_path.encode("utf-8")).hexdigest()


def pos_str(p):
    return "[%9.4f, %9.4f, %9.4f]" % (p[0], p[1], p[2])


def quat_str(q):
    return "[%9.6f, %9.6f, %9.6f, %9.6f]" % (q[0], q[1], q[2], q[3])


BONES = ["root", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head",
         "clavicle_l", "upperarm_l", "lowerarm_l", "hand_l", "thigh_l", "calf_l", "foot_l"]


def track_stats(track):
    pmax = 0.0
    for p in track["positions"]:
        for v in p:
            if abs(v) > pmax:
                pmax = abs(v)
    rmax = 0.0
    for q in track["rotations"]:
        d = max(abs(q[0]), abs(q[1]), abs(q[2]), abs(q[3] - 1.0))
        if d > rmax:
            rmax = d
    return pmax, rmax


def main():
    look_id = "c4bba06be47ce74153c2048d0de7b30a6465a84f"
    look = load_json(os.path.join(RAW, look_id + ".json"))
    print("== Look_D_Sweep header ==")
    print("source:", look["source"])
    print("keys:", look["sampledKeyCount"], "rate:", look["frameRateNumerator"], "/", look["frameRateDenominator"])

    ev = dict(look["evaluation"])
    transforms = ev.pop("retargetTransforms", None)
    print("== Look_D_Sweep evaluation (sans retargetTransforms) ==")
    for k in sorted(ev.keys()):
        v = ev[k]
        if isinstance(v, list) and len(v) > 8:
            v = "<list len=%d>" % len(v)
        print("  %-42s %s" % (k, v))
    print("  retargetTransforms len =", len(transforms) if transforms else 0)

    tracks = {}
    for t in look["tracks"]:
        tracks[t["bone"].lower()] = t
    print("track count:", len(look["tracks"]))

    print("== Look_D_Sweep frame-0 pose of key bones ==")
    for b in BONES:
        t = tracks.get(b)
        if not t:
            print("  %-10s MISSING" % b)
            continue
        print("  %-10s pos=%s rot=%s" % (b, pos_str(t["positions"][0]), quat_str(t["rotations"][0])))

    zp = 0
    zr = 0
    n = 0
    max_pos_bone = ("", 0.0)
    max_rot_bone = ("", 0.0)
    for t in look["tracks"]:
        n += 1
        pmax, rmax = track_stats(t)
        if pmax < 1e-6:
            zp += 1
        if rmax < 1e-6:
            zr += 1
        if pmax > max_pos_bone[1]:
            max_pos_bone = (t["bone"], pmax)
        if rmax > max_rot_bone[1]:
            max_rot_bone = (t["bone"], rmax)
    print("== Look_D_Sweep whole-clip stats ==")
    print("  bones total=%d  all-zero-position=%d  all-identity-rotation=%d" % (n, zp, zr))
    print("  max |position| bone: %s = %.4f cm" % (max_pos_bone[0], max_pos_bone[1]))
    print("  max |rot dev| bone:  %s = %.6f" % (max_rot_bone[0], max_rot_bone[1]))

    base_path = ev.get("baseAsset")
    base_frame = ev.get("baseFrame")
    print("== additive base ==")
    print("  baseAsset:", base_path)
    print("  baseFrame:", base_frame)
    if base_path:
        bid = sha1_path(base_path)
        bfile = os.path.join(RAW, bid + ".json")
        print("  base raw file:", bid, "exists:", os.path.exists(bfile))
        if os.path.exists(bfile):
            base = load_json(bfile)
            print("  base source:", base["source"])
            print("  base keys:", base["sampledKeyCount"], "rate:", base["frameRateNumerator"], "/", base["frameRateDenominator"])
            bt = {}
            for t in base["tracks"]:
                bt[t["bone"].lower()] = t
            idx = int(base_frame) if base_frame is not None else 0
            print("  == base pose at frame %d ==" % idx)
            for b in BONES:
                t = bt.get(b)
                if not t:
                    continue
                pi = min(idx, len(t["positions"]) - 1)
                ri = min(idx, len(t["rotations"]) - 1)
                print("    %-10s pos=%s rot=%s" % (b, pos_str(t["positions"][pi]), quat_str(t["rotations"][ri])))

    # Scan every raw file, classify additive assets.
    print("== full raw_sequences additive scan ==")
    additive = []
    total = 0
    for fn in sorted(os.listdir(RAW)):
        if not fn.endswith(".json"):
            continue
        total += 1
        data = load_json(os.path.join(RAW, fn))
        e = data.get("evaluation") or {}
        at = e.get("additiveType")
        if at is None:
            continue
        additive.append({
            "file": fn[:12],
            "source": data["source"],
            "additiveType": at,
            "basePoseType": e.get("basePoseType"),
            "baseAsset": e.get("baseAsset"),
            "baseFrame": e.get("baseFrame"),
            "keys": data["sampledKeyCount"],
        })
    print("  scanned files:", total, " additive:", len(additive))
    bases = {}
    for a in additive:
        bases.setdefault(a["baseAsset"], []).append(a["source"].split("/")[-1])
    print("  == additive -> base grouping ==")
    for bp, users in sorted(bases.items(), key=lambda kv: -len(kv[1])):
        print("  base %-90s users=%d" % (bp, len(users)))
        for u in sorted(users):
            print("      ", u)
    print("  == per-additive detail ==")
    for a in additive:
        print("  %-70s type=%-22s basePose=%-14s baseFrame=%-4s base=%s" % (
            a["source"].split("/")[-1], a["additiveType"], str(a["basePoseType"]), str(a["baseFrame"]),
            (a["baseAsset"] or "").split("/")[-1]))

    print("ANALYZE_ADDITIVE_DELTA_DONE")


if __name__ == "__main__":
    main()

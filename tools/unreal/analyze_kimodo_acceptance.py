"""Analyze saved UE playback evidence; no Unreal dependency."""
import ast
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "assets/generated/kimodo_ue/acceptance"
assets = json.loads((OUT / "asset_poses.json").read_text())
pairs = {"VB root_foot_l": "foot_l", "VB root_foot_r": "foot_r",
         "VB pelvis_hand_l": "hand_l", "VB hand_r_hand_l": "hand_l"}


def distance(a, b):
    return float(np.linalg.norm(np.asarray(a) - b))


report = {"units": "cm, seconds", "runtime": {}}
for hz in (30, 60, 120):
    data = json.loads((OUT / f"runtime_{hz}.json").read_text())
    result = {}
    for key, start in data["starts"].items():
        rows = [r["characters"][key] for r in data["rows"]]
        samples = assets[key]["samples"]
        errors = []
        for row in rows:
            t = min(120.0, row["time"] * 30)
            i = min(119, int(t))
            root = np.asarray(samples[i]["root"]["p"]) * (1 - (t - i)) + np.asarray(samples[i+1]["root"]["p"]) * (t-i)
            expected = [root[1], -root[0], 0]  # mesh yaw -90
            errors.append(distance(np.asarray(row["actor"]) - start, expected))
        vb_error = max((distance(r["bonesWorld"][vb]["p"], r["bonesWorld"][target]["p"])
                        for r in rows for vb, target in pairs.items()), default=0) if key == "virtual" else 0
        result[key] = {"samples": len(rows), "finalTime": rows[-1]["time"],
                       "actorDelta": (np.asarray(rows[-1]["actor"]) - start).tolist(),
                       "maxRootMotionErrorCm": max(errors),
                       "maxComponentRootCm": max(distance(r["rootComponent"]["p"], [0,0,0]) for r in rows),
                       "maxVirtualTargetErrorCm": vb_error,
                       "walkingEveryFrame": all("MOVE_WALKING" in r["movementMode"] for r in rows),
                       "minimumToeWorldHeightAboveFloorCm": min(r["bonesWorld"][b]["p"][2]-10000 for r in rows for b in ("ball_l","ball_r"))}
        result[key]["rootMotionPassed"] = max(errors) < .01 and result[key]["maxComponentRootCm"] < .001 and result[key]["walkingEveryFrame"] and rows[-1]["time"] == 4
        if key == "virtual":
            result[key]["virtualBonesWithinPointOneCm"] = vb_error < .1
    report["runtime"][str(hz)] = result

npz = np.load(OUT.parent / "walk_stop_42.npz")
contacts = npz["foot_contacts"][:119]
# Parse joint ordering without importing Kimodo / torch.
tree = ast.parse(Path("G:/Github/kimodo/kimodo/skeleton/definitions.py").read_text())
cls = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == "SOMASkeleton77")
order = ast.literal_eval(next(n.value for n in cls.body if isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "bone_order_names_with_parents" for t in n.targets)))
names = [x[0] for x in order]
report["contactMetrics"] = {}
for bone, source, channel in (("foot_l","LeftFoot",0),("ball_l","LeftToeBase",1),("foot_r","RightFoot",3),("ball_r","RightToeBase",4)):
    target = np.asarray([s[bone]["p"] for s in assets["fbx"]["samples"]])[:120]
    original = npz["posed_joints"][:,names.index(source)] * 100
    mask = contacts[:,channel]
    row = {}
    for label, values, axes in (("soma", original, [0,2]),("manny",target,[0,1])):
        speeds = np.linalg.norm(np.diff(values[:,axes],axis=0),axis=1)*30
        row[label] = {"contactMeanHorizontalCmPerSec":float(speeds[mask].mean()),"contactMaxHorizontalCmPerSec":float(speeds[mask].max())}
    # Only intervals with at least three contact samples; measure total drift,
    # including changing direction, rather than endpoint displacement alone.
    intervals = []
    full = npz["foot_contacts"][:,channel]
    edges = np.diff(np.r_[False,full,False].astype(int))
    for a,b in zip(np.where(edges==1)[0], np.where(edges==-1)[0]):
        if b-a >= 3:
            p = target[a:b,:2]
            intervals.append({"firstFrame":int(a),"lastFrame":int(b-1),
                              "pathCm":float(np.linalg.norm(np.diff(p,axis=0),axis=1).sum()),
                              "diameterCm":float(np.linalg.norm(p[:,None]-p[None,:],axis=2).max())})
    row["contactIntervals"] = intervals
    report["contactMetrics"][bone] = row
report["footQuality"] = {"strictNoSlipAccepted": False,
    "reason": "Contact-marked ankle/toe proxies retain nonzero world drift; left toe can fall below fixture floor. These are bone proxies, not skinned sole vertices. No terrain IK or foot-lock correction was applied."}
report["virtualBoneDefinitions"] = json.loads((OUT / "assets.json").read_text())["virtualBones"]
report["rootMotionToleranceCm"] = .01
report["virtualBoneFollowProbeToleranceCm"] = .1
report["virtualBoneFollowProbeNote"] = "A strict follow probe, not a universal UE quality standard; compare keyframe and subframe errors separately."
(OUT / "results.json").write_text(json.dumps(report, indent=2),encoding="utf-8")
print(json.dumps(report["runtime"],indent=2))
print(json.dumps(report["contactMetrics"],indent=2))

frames = [1,30,60,85,90,120]
if all((OUT / f"played_f{f:03}.png").exists() for f in frames):
    sheet = Image.new("RGB",(1800,1155),(24,27,33))
    draw = ImageDraw.Draw(sheet)
    for i,f in enumerate(frames):
        im = Image.open(OUT / f"played_f{f:03}.png").convert("RGB")
        # Identical crop of every original; remove empty sky and foreground only.
        im = im.crop((0,int(im.height*.3),im.width,int(im.height*.9)))
        im.thumbnail((895,340))
        x,y = (i%2)*900,(i//2)*385
        sheet.paste(im,(x,y+38))
        draw.text((x+12,y+12),f"Frame {f:03} | {f/30:.3f}s | Native / FBX / FBX+VB",fill="white")
    sheet.save(OUT / "root_motion_contact_sheet.png")

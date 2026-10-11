import json

with open("F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/fbx_als/root_motion_shapes.json", "r", encoding="utf-8") as f:
    data = json.load(f)

rows = []
for clip, layers in data["clips"].items():
    orig = layers.get("orig", {})
    regen = layers.get("regen", {})
    rootik = layers.get("rootik", {})
    final = layers.get("final", {})
    op = (orig.get("spread", {}).get("pelvis") or ["-"] * 3)
    fp = (final.get("spread", {}).get("pelvis") or ["-"] * 3)
    rr = (rootik.get("spread", {}).get("root") or ["-"] * 3)
    rows.append((
        clip,
        "orig_rm=%s" % orig.get("rootMotion"),
        "orig_pelvis=%s" % op,
        "rootik_root=%s" % rr,
        "final_rm=%s" % final.get("rootMotion"),
        "final_pelvis=%s" % fp,
        "final_root=%s" % (final.get("spread", {}).get("root") or ["-"] * 3),
    ))

for r in rows:
    print(" | ".join(str(x) for x in r))

n = len(rows)
same_sign = 0
report = []
for clip, layers in data["clips"].items():
    orig = layers.get("orig", {})
    final = layers.get("final", {})
    op = orig.get("spread", {}).get("pelvis")
    fp = final.get("spread", {}).get("pelvis")
    if not op or not fp:
        continue
    if op[1] > 1e-3 and fp[1] > 1e-3:
        same_sign += 1
        report.append((clip, round(op[1], 1), round(fp[1], 1), round(fp[1] / op[1], 3) if op[1] > 1e-6 else None))
print()
print("pelvis-Y-forward-magnitude pairs (orig vs final):")
for t in report:
    print(t)
print("total clips=%d pairs_with_forward_motion=%d" % (n, len(report)))

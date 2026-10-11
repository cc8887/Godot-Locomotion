"""Cross-map original ALS manifest animations against Kimodo ALS_Final 123 clips."""
import json

MANIFEST = "F:/Github/Godot-Locomotion/assets/generated/als_v4/als_manifest.json"
FINAL_DIR = "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/fbx_als"

import os
final_clips = []
for f in os.listdir(FINAL_DIR):
    if f.endswith(".fbx"):
        final_clips.append(f[:-4])

with open(MANIFEST, "r", encoding="utf-8") as f:
    m = json.load(f)

anim_by_name = {}
for e in m["animations"]:
    anim_by_name[e["assetName"]] = e

matched, missing_in_final, extra_in_final = [], [], []
for name, e in anim_by_name.items():
    if name in final_clips:
        matched.append(name)
    else:
        missing_in_final.append(name)

for c in final_clips:
    if c not in anim_by_name:
        extra_in_final.append(c)

print("FINAL FBX:", len(final_clips))
print("MANIFEST ANIMS:", len(anim_by_name))
print("MATCHED:", len(matched))
print("MISSING IN FINAL (manifest anims without Kimodo clip):", len(missing_in_final))
for n in sorted(missing_in_final):
    print("  -", n, "|", anim_by_name[n]["objectPath"])
print("EXTRA IN FINAL (Kimodo clips not in manifest):", len(extra_in_final))
for n in sorted(extra_in_final):
    print("  -", n)

# Check missing ones' objectPath prefixes to understand categories
cats = {}
for n in missing_in_final:
    p = anim_by_name[n]["objectPath"]
    cat = "/".join(p.split("/")[4:7])
    cats.setdefault(cat, []).append(n)
print("MISSING BY CATEGORY:")
for c, ns in sorted(cats.items()):
    print("  %-60s %d" % (c, len(ns)))

# rootMotionEnabled flags of the matched 123
rm_true = [n for n in matched if anim_by_name[n]["metadata"].get("rootMotionEnabled")]
print("MATCHED rm=True:", len(rm_true), rm_true)
# objectPath → source mapping: Kimodo source would be /Game/KimodoMocara/ALS_Final/<name>
# assetId = sha1(UTF8(objectPath)) — new ids for Kimodo objects
print("DONE")

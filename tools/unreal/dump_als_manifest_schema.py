"""Dump the structure of the original als_manifest.json for Kimodo integration planning."""
import json

MANIFEST = "F:/Github/Godot-Locomotion/assets/generated/als_v4/als_manifest.json"

with open(MANIFEST, "r", encoding="utf-8") as f:
    m = json.load(f)

print("TOP KEYS:", sorted(m.keys()))
for k, v in m.items():
    if isinstance(v, list):
        print("  %-24s list len=%d" % (k, len(v)))
    elif isinstance(v, dict):
        print("  %-24s dict keys=%s" % (k, sorted(v.keys())[:10]))
    else:
        print("  %-24s %r" % (k, v))

# Schema of one animation entry (pick a few interesting ones)
anim_keys = None
if "animations" in m:
    entries = m["animations"]
else:
    # find the list that holds animation-like dicts
    for k, v in m.items():
        if isinstance(v, list) and v and isinstance(v[0], dict) and "objectPath" in str(v[0])[:2000]:
            entries = v
            print("ANIMATION-LIKE LIST:", k)
            break

SAMPLES = ["ALS_N_JumpRun_LF", "ALS_N_LandRoll_F", "ALS_N_Walk_F"]
for e in m.get("animations", []):
    name = e.get("name") or e.get("assetName") or ""
    if name in SAMPLES or e.get("objectPath", "").endswith(name):
        pass

# fallback: iterate to find by object path substring
def find_entry(sub):
    for e in m.get("animations", []):
        if sub in json.dumps(e)[:4000]:
            return e
    return None

for s in SAMPLES:
    e = find_entry(s)
    if e is None:
        print("NOT FOUND:", s)
        continue
    print("=" * 30, s)
    for k, v in e.items():
        if isinstance(v, list):
            print("  %-22s list len=%d first=%s" % (k, len(v), json.dumps(v[0])[:300] if v else "[]"))
        elif isinstance(v, dict):
            print("  %-22s dict %s" % (k, json.dumps(v)[:300]))
        else:
            print("  %-22s %r" % (k, v))

# Montage-like entries
for k, v in m.items():
    if isinstance(v, list) and v and isinstance(v[0], dict):
        first = json.dumps(v[0])
        if "montage" in k.lower() or "segments" in first.lower():
            print("=" * 30, "LIST", k)
            print(json.dumps(v[0], indent=1)[:2000])

# Files entry
if "files" in m:
    print("=" * 30, "FILES[0..3]")
    for row in m["files"][:3]:
        print(json.dumps(row))
    anim_files = [r for r in m["files"] if "ALS_N_JumpRun_LF" in json.dumps(r)]
    print("JumpRun file rows:", json.dumps(anim_files[:2], indent=1))

# Count by type
if "animations" in m:
    kinds = {}
    for e in m["animations"]:
        kinds[e.get("kind") or e.get("type") or "?"] = kinds.get(e.get("kind") or e.get("type") or "?", 0) + 1
    print("ANIMATION KINDS:", kinds)
    print("ANIMATION TOTAL:", len(m["animations"]))

print("DONE")

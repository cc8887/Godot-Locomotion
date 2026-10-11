"""Verify the synthesized Kimodo manifest preserves all structural invariants.

Checks:
  1. Animation ids equal sha1(objectPath) for every entry.
  2. Montage segment animationId references resolve against animations.
  3. Animation dependencies resolve.
  4. rm/forceRootLock/rootLock flags match the ORIGINAL manifest per asset
     (flag inheritance rule).
  5. Kimodo-replaced entries' files rows carry the sidecar fbxSha256.
  6. Every manifest files row's relativePath exists in the Kimodo tree and
     the on-disk size matches; 3 kept assets still point at original FBX
     content (their sha256 rows stay original too).
  7. Output tree contains no leftover original-animation FBX for replaced
     assets (they were overwritten by the Kimodo copies).
"""

import hashlib
import json
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
ORIGINAL_ROOT = PROJECT / "assets" / "generated" / "als_v4"
KIMODO_FBX_DIR = PROJECT / "assets" / "generated" / "kimodo_ue" / "als_regen" / "fbx_als"
TREE = Path(sys.argv[1]) if len(sys.argv) > 1 else PROJECT / "assets" / "generated" / "kimodo_als"

FLAG_KEYS = ("rootMotionEnabled", "forceRootLock", "rootMotionRootLock",
             "useNormalizedRootMotionScale", "loop", "interpolation", "additiveType")


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    manifest = load(TREE / "als_manifest.json")
    original = load(ORIGINAL_ROOT / "als_manifest.json")
    errors = []

    orig_by_name = {a["assetName"]: a for a in original["animations"]}
    kimodo_sidecars = {p.name[: -len(".fbx.source.json")]: load(p)
                       for p in KIMODO_FBX_DIR.glob("*.fbx.source.json")}

    anims = manifest["animations"]
    by_id = {a["id"]: a for a in anims}
    files_by_path = {f["relativePath"]: f for f in manifest["files"]}
    for section in ("skeletons", "skeletalMeshes", "staticMeshes", "blendSpaces",
                    "aimOffsets", "materials", "textures", "physicsAssets",
                    "curves", "configAssets", "montages"):
        for entry in manifest.get(section, []):
            by_id[entry["id"]] = entry

    # 1. id == sha1(objectPath)
    for a in anims:
        expected = hashlib.sha1(a["objectPath"].encode("utf-8")).hexdigest()
        if a["id"] != expected:
            errors.append(f"id mismatch for {a['assetName']}")
    # 2. montage references resolve
    for m in manifest["montages"]:
        for slot in m["metadata"]["slots"]:
            for seg in slot["segments"]:
                if seg["animationId"] not in by_id:
                    errors.append(f"montage {m['assetName']} segment animationId {seg['animationId']} unresolved")
    # 3. animation dependencies resolve
    for a in anims:
        for dep in a.get("dependencies", []):
            if dep not in by_id:
                errors.append(f"animation {a['assetName']} dependency {dep} unresolved")
    # 4/5. flags inherited + files rows updated
    replaced = kept = 0
    for a in anims:
        name = a["assetName"]
        orig_meta = orig_by_name[name]["metadata"]
        meta = a["metadata"]
        for key in FLAG_KEYS:
            if meta.get(key) != orig_meta.get(key):
                errors.append(f"flag {key} changed for {name}: {meta.get(key)} != {orig_meta.get(key)}")
        row = files_by_path.get(a["outputPath"])
        if row is None:
            errors.append(f"files row missing for {name}")
            continue
        sidecar = kimodo_sidecars.get(name)
        if sidecar is None:
            kept += 1
            orig_row = {f["relativePath"]: f for f in original["files"]}[a["outputPath"]]
            if row["sha256"] != orig_row["sha256"]:
                errors.append(f"kept asset {name} sha256 changed")
        else:
            replaced += 1
            if row["sha256"] != sidecar["fbxSha256"]:
                errors.append(f"files row sha256 != sidecar for {name}")
            if row.get("size") != (TREE / a["outputPath"]).stat().st_size:
                errors.append(f"files row size mismatch on disk for {name}")
    # 6. every files row exists on disk with matching size
    for f in manifest["files"]:
        target = TREE / f["relativePath"]
        if not target.is_file():
            errors.append(f"file missing on disk: {f['relativePath']}")
        elif target.stat().st_size != f["size"]:
            errors.append(f"size mismatch: {f['relativePath']} manifest={f['size']} disk={target.stat().st_size}")
    # 7. replaced animations on disk match Kimodo sha256 (spot-check hashes)
    for a in anims:
        sidecar = kimodo_sidecars.get(a["assetName"])
        if sidecar is None:
            continue
        digest = hashlib.sha256((TREE / a["outputPath"]).read_bytes()).hexdigest()
        if digest != sidecar["fbxSha256"]:
            errors.append(f"on-disk FBX sha256 != sidecar for {a['assetName']}")

    summary = {
        "animations": len(anims),
        "replaced": replaced,
        "kept": kept,
        "files": len(manifest["files"]),
        "montages": len(manifest["montages"]),
        "errors": errors,
    }
    print("KIMODO_MANIFEST_VERIFY " + ("OK" if not errors else "FAILED") + " " + json.dumps(summary))
    if errors:
        sys.exit(1)


if __name__ == "__main__":
    main()

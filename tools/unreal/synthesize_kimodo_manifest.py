"""Synthesize the Kimodo variant of the ALS manifest for Godot P2B import.

Base: the original GASP58 ALS manifest (126 animations).
Changes:
  * For the 123 assets that Kimodo regenerated: repoint outputPath to the
    Kimodo FBX, replace the files-row sha256/size with the Kimodo values,
    keep the original objectPath/id so every montage/configAsset/raw-source
    cross reference stays valid without rewrites.
  * Data-driven metadata (playLength/frameRate*/sampledKeyCount) is updated
    from the Kimodo .fbx.source.json sidecars where available.
  * Runtime flag metadata (rootMotionEnabled/forceRootLock/rootMotionRootLock/
    useNormalizedRootMotionScale/loop/interpolation/additiveType) is inherited
    from the original manifest entries so the Godot compile gates and runtime
    branches keep production semantics (original rm=True list:
    ALS_N_LandRoll_F, ALS_CLF_GetUp_Back, ALS_CLF_GetUp_Front).
  * The 3 assets Kimodo did not regenerate (ALS_Mannequin_T_Pose and the two
    additive Land variants) keep their whole entries and original FBX files.
  * sourceProjectId gets a Kimodo marker (sourceProjectId stays, a new
    kimodoSource marker field is added) so the gate logic can assert the
    running set comes from Kimodo.

Output: <output-root>/als_manifest.json plus copied asset tree.
"""

import json
import shutil
import sys
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
ORIGINAL_ROOT = PROJECT / "assets" / "generated" / "als_v4"
KIMODO_FBX_DIR = PROJECT / "assets" / "generated" / "kimodo_ue" / "als_regen" / "fbx_als"
DEFAULT_OUTPUT = PROJECT / "assets" / "generated" / "kimodo_als"

FLAG_KEYS = (
    "rootMotionEnabled",
    "rootMotionRootLock",
    "forceRootLock",
    "useNormalizedRootMotionScale",
    "loop",
    "interpolation",
    "additiveType",
)
DATA_KEYS = ("playLength", "frameRateNumerator", "frameRateDenominator", "sampledKeyCount")


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    output_root = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_OUTPUT
    manifest = load_json(ORIGINAL_ROOT / "als_manifest.json")

    kimodo_by_name = {}
    for sidecar in sorted(KIMODO_FBX_DIR.glob("*.fbx.source.json")):
        kimodo_by_name[sidecar.name[: -len(".fbx.source.json")]] = sidecar
    print(f"kimodo sidecars: {len(kimodo_by_name)}")

    animations = manifest["animations"]
    original_files = {row["relativePath"]: row for row in manifest["files"]}
    replaced = kept = 0
    swapped = {}

    for entry in animations:
        name = entry["assetName"]
        sidecar_path = kimodo_by_name.get(name)
        if sidecar_path is None:
            kept += 1
            swapped[name] = ("original", entry["outputPath"])
            continue
        sidecar = load_json(sidecar_path)
        fbx_path = Path(sidecar["fbxPath"])
        if not fbx_path.is_file():
            raise RuntimeError(f"Kimodo FBX missing for {name}: {fbx_path}")
        original_output = entry["outputPath"]
        # outputPath stays under the same relative layout (animations/...)
        # so only the file content changes; the manifest row keeps its path.
        old_file_row = original_files.get(original_output)
        if old_file_row is None:
            raise RuntimeError(f"Manifest files row missing for {name}: {original_output}")
        size = fbx_path.stat().st_size
        new_row = dict(old_file_row)
        new_row["sha256"] = sidecar["fbxSha256"]
        new_row["size"] = size
        original_files[original_output] = new_row
        metadata = entry["metadata"]
        sidecar_meta = sidecar.get("metadata")
        if sidecar_meta:
            for key in DATA_KEYS:
                if key in sidecar_meta:
                    metadata[key] = sidecar_meta[key]
        # Runtime flags are inherited from the original entry (already in
        # metadata); we only assert they stay untouched.
        entry["kimodoSource"] = {
            "pipeline": sidecar.get("pipeline"),
            "sourceAnimPath": sidecar.get("sourceAnimPath"),
            "retargetedAssetPath": sidecar.get("retargetedAssetPath"),
            "fbxSha256": sidecar.get("fbxSha256"),
        }
        replaced += 1
        swapped[name] = ("kimodo", original_output, sidecar["fbxSha256"], size)

    manifest["files"] = [original_files[row_path] for row_path in
                         (r["relativePath"] for r in manifest["files"])]
    manifest["kimodoSourceMarker"] = {
        "variant": "kimodo-als-regen",
        "generatedBy": "synthesize_kimodo_manifest.py",
        "replacedAnimations": replaced,
        "keptOriginalAnimations": kept,
    }

    output_root.mkdir(parents=True, exist_ok=True)
    (output_root / "als_manifest.json").write_text(
        json.dumps(manifest, indent=2, allow_nan=False) + "\n", encoding="utf-8")

    # Assemble the asset tree: copy everything from the original root except
    # the manifest itself and the compiled directory; Kimodo FBX files replace
    # the animation FBX files at their manifest outputPath locations.
    copied = skipped_replaced = 0
    for item in ORIGINAL_ROOT.rglob("*"):
        if item.is_dir():
            continue
        rel = item.relative_to(ORIGINAL_ROOT).as_posix()
        if rel == "als_manifest.json" or rel.startswith("compiled/"):
            continue
        destination = output_root / rel
        destination.parent.mkdir(parents=True, exist_ok=True)
        copied += 1
        shutil.copy2(item, destination)

    for entry in animations:
        name = entry["assetName"]
        sidecar_path = kimodo_by_name.get(name)
        rel = entry["outputPath"]
        destination = output_root / rel
        destination.parent.mkdir(parents=True, exist_ok=True)
        if sidecar_path is None:
            src = ORIGINAL_ROOT / rel
            if not src.is_file():
                raise RuntimeError(f"Original FBX missing for kept asset {name}: {src}")
            shutil.copy2(src, destination)
        else:
            sidecar = load_json(sidecar_path)
            shutil.copy2(Path(sidecar["fbxPath"]), destination)
            skipped_replaced += 1

    summary = {
        "outputRoot": str(output_root),
        "manifestAnimations": len(animations),
        "replacedWithKimodo": replaced,
        "keptOriginal": kept,
        "filesCopiedFromOriginal": copied,
        "kimodoFbxPlaced": skipped_replaced,
        "manifestPath": str(output_root / "als_manifest.json"),
    }
    (output_root / "kimodo_tree_summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print("KIMODO_MANIFEST_SYNTH_OK " + json.dumps(summary))


if __name__ == "__main__":
    main()

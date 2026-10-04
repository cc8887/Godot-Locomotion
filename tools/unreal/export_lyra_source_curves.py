"""Export full source curves and real raw/additive curve evaluations without saving assets."""
import hashlib
import json
import math
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
if not root.is_absolute() or not root.is_dir():
    raise ValueError("Existing absolute LYRA_OUTPUT_ROOT required")
destination = root / "logical_controls"
sha = lambda data: hashlib.sha256(data).hexdigest()
catalog_bytes = (destination / "catalog.json").read_bytes()
calibration_bytes = (destination / "calibration.json").read_bytes()
catalog, calibration = json.loads(catalog_bytes), json.loads(calibration_bytes)
content = Path(unreal.Paths.project_content_dir())

def package_hash(path):
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())

def save(path, value):
    if path.exists():
        if json.loads(path.read_bytes()) != value:
            raise ValueError("Existing curve export differs: " + str(path))
    else:
        path.write_text(json.dumps(value, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())

assets = calibration["assetSha256"]
if any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Source calibration package changed")
slots_by_target = {row["target"]: row["slot"] for row in catalog["entries"]}
entries, native = [], []

def export(slot, animation, metadata, source, target, base_slot, length_override=None):
    length = metadata["sequencePlayLength"] if length_override is None else length_override
    curves = json.loads(unreal.AlsSourceAnimationLibrary.read_source_float_curves(animation))["curves"]
    times = {0.0, length}
    for hz in (30, 60, 120):
        times.update(index / hz for index in range(math.floor(length * hz) + 1))
    for curve in curves:
        keys = curve["keys"]
        times.update(key["time"] for key in keys if 0 <= key["time"] <= length)
        times.update((a["time"] + b["time"]) * .5 for a, b in zip(keys, keys[1:]) if b["time"] <= length)
        for key in keys:
            bits = struct.unpack("<I", struct.pack("<f", key["time"]))[0]
            for offset in (-1, 1):
                if 0 <= bits + offset <= 0x7f7fffff:
                    value = struct.unpack("<f", struct.pack("<I", bits + offset))[0]
                    if 0 <= value <= length:
                        times.add(value)
    trace = json.loads(unreal.AlsLyraGraphLibrary.read_source_curve_trace(animation, json.dumps(sorted(times))))
    rows = trace["rows"]
    for curve in trace["metadata"]["curves"]:
        name = curve["name"]
        if any(name not in row["raw"] for row in rows):
            raise ValueError("Authored curve absent in native raw evaluation: " + name)
        flags = {row["raw"][name]["flags"] for row in rows}
        if len(flags) != 1:
            raise ValueError("Time-varying curve flags are not supported")
        curve["elementFlags"] = flags.pop()
    additive = metadata["additiveType"] != "AAT_None"
    if additive and (metadata["basePoseType"] != "ABPT_AnimFrame" or metadata["baseFrame"] != 0):
        raise ValueError("This closure requires frame-zero additive base")
    row = {"slot": slot, "source": source, "target": target, "playLength": length,
           "additive": additive, "baseSlot": base_slot, "curves": trace["metadata"]["curves"],
           "attributes": trace["metadata"]["attributes"], "transformCurves": metadata["transformCurveCount"]}
    if len(row["attributes"]) != metadata["animatedBoneAttributeCount"] or row["transformCurves"]:
        raise ValueError("Do not silently discard source attributes or transform curves")
    native.extend({"slot": slot, **sample} for sample in rows)
    unreal.log(f"LYRA_SOURCE_CURVE_CLIP slot={slot} curves={len(row['curves'])} samples={len(rows)}")
    return row

for entry in catalog["entries"]:
    clip = json.loads((destination / entry["file"]).read_bytes())
    animation = unreal.load_asset(entry["target"])
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
    base_slot = slots_by_target[metadata["baseAsset"]] if entry["additive"] else None
    if (metadata["sequencePlayLength"] != clip["metadata"]["sequencePlayLength"] or
        metadata["floatCurveNames"] != clip["metadata"]["floatCurveNames"]):
        raise ValueError("Original target and extended logical metadata differ")
    entries.append(export(entry["slot"], animation, metadata, entry["source"], entry["target"], base_slot, entry["playLength"]))

center = next(e for e in catalog["entries"] if e["slot"].startswith("aim_unarmed_") and e["point"][:2] == [0, 0])
fixture = unreal.AlsLyraGraphLibrary.create_source_curve_fixture(unreal.load_asset(center["target"]))
if fixture is None:
    raise ValueError("Could not construct transient additive curve fixture")
base = fixture.get_editor_property("ref_pose_seq")
fixtures = []
for slot, animation, base_slot in [("fixture_base", base, None), ("fixture_additive", fixture, "fixture_base")]:
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
    fixtures.append(export(slot, animation, metadata, slot, slot, base_slot))

if any(package_hash(path) != value for path, value in assets.items()):
    raise ValueError("Read/fixture construction changed a source package")
bank = {"schemaVersion": 1, "catalogSha256": sha(catalog_bytes), "calibrationSha256": sha(calibration_bytes),
        "entries": entries, "fixtures": fixtures}
bank_sha = save(destination / "curve_bank.json", bank)
save(destination / "curve_native.json", {"schemaVersion": 1, "curveBankSha256": bank_sha, "rows": native})
unreal.log(f"LYRA_SOURCE_CURVES_OK clips={len(entries)} curves={sum(len(e['curves']) for e in entries)} native={len(native)} fixtures=2 assets_saved=0")

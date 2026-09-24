"""Batch original animation and linked-blueprint inputs; never save UE assets.
ALS_REFACTORED_SOURCE_OUTPUT selects the index. Payloads live beside it in
refactored_animation_sources/. Optional ALS_REFACTORED_SOURCE_QUIT=1 exits Editor.
"""
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unreal

output = Path(os.environ["ALS_REFACTORED_SOURCE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute index path required")
directory = output.parent / "refactored_animation_sources"
directory.mkdir(parents=True, exist_ok=True)

def decode(text):
    return json.loads(text, parse_int=lambda value: -0.0 if value == "-0" else int(value))

def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-refactored-source-") as temporary:
        task = unreal.AssetExportTask()
        task.object, task.exporter = asset, unreal.ObjectExporterT3D()
        task.filename = str(Path(temporary) / "asset.t3d")
        task.automated, task.prompt = True, False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native text export failed: " + asset.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")

def write(path, value):
    data = (json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")
    path.write_bytes(data)
    return hashlib.sha256(data).hexdigest()

registry = unreal.AssetRegistryHelpers.get_asset_registry()
roots = ["/ALS/ALS/Animations", "/ALS/ALS/Character/AnimationInstances", "/ALS/ALS/Data/AnimationInstance"]
registry.scan_paths_synchronous(["/ALS"], force_rescan=True)
assets = {}
for root in roots:
    for row in registry.get_assets_by_path(root, recursive=True):
        asset = row.get_asset()
        if not asset:
            raise RuntimeError("Cannot load source asset: " + str(row.package_name))
        assets[asset.get_path_name()] = asset
parent = unreal.load_asset("/ALS/ALS/Character/AB_Als.AB_Als")
parent_class = unreal.load_class(None, "/ALS/ALS/Character/AB_Als.AB_Als_C")
settings = unreal.get_default_object(parent_class).get_editor_property("settings")
for asset in (parent, settings):
    assets[asset.get_path_name()] = asset
rows, skeletons, counts = [], {}, {}
for path, asset in sorted(assets.items()):
    kind = asset.get_class().get_name()
    payload = {"schemaVersion": 1, "source": path, "class": kind, "nativeText": native_text(asset)}
    if isinstance(asset, unreal.AnimSequence):
        skeleton = asset.get_editor_property("skeleton")
        skeletons[skeleton.get_path_name()] = decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))
        payload["raw"] = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset))
        payload["evaluation"] = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset))
        payload["curves"] = decode(unreal.AlsSourceAnimationLibrary.read_source_float_curves(asset))
        if payload["evaluation"]["additiveType"] == "AAT_None":
            length = payload["raw"]["playLength"]
            payload["poseReference"] = [decode(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(
                asset, length * fraction, True, False, False)) for fraction in (0, .137, .5, .773, 1)]
    elif isinstance(asset, unreal.AnimBlueprint):
        payload["compiled"] = decode(unreal.AlsAnimationGraphLibrary.read_compiled_animation_graph(asset))
    elif isinstance(asset, unreal.BlendSpace):
        payload["samples"] = []
        for sample in asset.get_editor_property("sample_data"):
            sequence = sample.get_editor_property("animation")
            point = sample.get_editor_property("sample_value")
            payload["samples"].append({"sequence": sequence.get_path_name() if sequence else None,
                                       "point": [point.x, point.y, point.z]})
    elif isinstance(asset, unreal.AnimMontage):
        payload["curves"] = decode(unreal.AlsSourceAnimationLibrary.read_source_float_curves(asset))
    filename = hashlib.sha256(path.encode("utf-8")).hexdigest() + ".json"
    digest = write(directory / filename, payload)
    rows.append({"source": path, "class": kind, "file": "refactored_animation_sources/" + filename, "sha256": digest})
    counts[kind] = counts.get(kind, 0) + 1
    if len(rows) % 20 == 0:
        unreal.log(f"ALS_REFACTORED_SOURCES_PROGRESS exported={len(rows)}")
write(output, {"schemaVersion": 1, "roots": roots, "parentClass": parent_class.get_path_name(),
               "settings": settings.get_path_name(), "counts": counts, "assets": rows, "skeletons": skeletons})
unreal.log(f"ALS_REFACTORED_SOURCES_OK assets={len(rows)} classes={counts} assets_saved=0")
if os.environ.get("ALS_REFACTORED_SOURCE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

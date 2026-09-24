"""Read actual Head settings, Look source closure and raw native BlendSpace poses."""
import hashlib
import json
import os
from pathlib import Path
import tempfile
import unreal

root = Path(__file__).parents[2]
output = Path(os.environ["ALS_HEAD_INPUTS"])
reference = Path(os.environ["ALS_HEAD_REFERENCE"])
if not output.is_absolute() or not reference.is_absolute() or output == reference:
    raise ValueError("Distinct absolute output paths required")

def decode(text):
    return json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))

def native_text(asset):
    with tempfile.TemporaryDirectory(prefix="als-head-inputs-") as temporary:
        task = unreal.AssetExportTask()
        task.object, task.exporter = asset, unreal.ObjectExporterT3D()
        task.filename = str(Path(temporary) / "asset.t3d")
        task.automated, task.prompt = True, False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native text export failed")
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")

parent_class = unreal.load_class(None, "/ALS/ALS/Character/AB_Als.AB_Als_C")
settings = unreal.get_default_object(parent_class).get_editor_property("settings")
head = settings.get_editor_property("head")
properties = ("pitch_angle_interpolation_half_life", "yaw_angle_interpolation_half_life",
              "switch_look_sides_yaw_angle_interpolation_half_life",
              "first_person_pitch_angle_interpolation_half_life", "first_person_yaw_angle_interpolation_half_life")
space = unreal.load_asset("/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look")
samples = []
pending = []
for sample in space.get_editor_property("sample_data"):
    sequence = sample.get_editor_property("animation")
    point = sample.get_editor_property("sample_value")
    samples.append({"sequence": sequence.get_path_name(), "point": [point.x, point.y, point.z]})
    pending.append(sequence)
sequences, skeletons, seen = [], {}, set()
while pending:
    sequence = pending.pop(0)
    path = sequence.get_path_name()
    if path in seen:
        continue
    seen.add(path)
    base = sequence.get_editor_property("ref_pose_seq")
    if base:
        pending.append(base)
    skeleton = sequence.get_editor_property("skeleton")
    skeletons[skeleton.get_path_name()] = {"metadata": decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton))}
    sequences.append({"raw": decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(sequence)),
                      "evaluation": decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence)),
                      "curves": decode(unreal.AlsSourceAnimationLibrary.read_source_float_curves(sequence))})
result = {"schemaVersion": 1, "parentClass": parent_class.get_path_name(),
          "graphsSha256": hashlib.sha256((root / "assets/config/refactored_layering_graphs.json").read_bytes()).hexdigest(),
          "settings": {"source": settings.get_path_name(), "head": {name: head.get_editor_property(name) for name in properties}},
          "blendSpace": {"source": space.get_path_name(), "nativeText": native_text(space), "samples": samples},
          "sequences": sequences, "skeletons": skeletons}

def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")

write(output, result)
poses = [decode(unreal.AlsSourceAnimationLibrary.read_raw_blend_space_pose(space, pitch, time))
         for pitch in (-120, -90, -45, 0, 45, 90, 120) for time in (0, .137, .5, .773, 1)]
write(reference, {"schemaVersion": 1, "inputsSha256": hashlib.sha256(output.read_bytes()).hexdigest(), "poses": poses})
unreal.log(f"ALS_REFACTORED_HEAD_INPUTS_OK samples={len(samples)} sequences={len(sequences)} poses={len(poses)} assets_saved=0")
if os.environ.get("ALS_HEAD_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

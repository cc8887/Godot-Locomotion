"""Read ALS Refactored camera graphs/settings/curves; never save UE assets."""
import json
import os
import tempfile
import struct
from pathlib import Path
import unreal

output = Path(os.environ["ALS_CAMERA_INPUTS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
root = "/ALS/ALSCamera/"
paths = [root + name for name in (
    "AB_Als_Camera", "B_Als_CameraComponent", "Data/CS_Als_Default",
    "Data/CF_Als_CameraBlend_Smooth", "Data/CF_Als_CameraBlend_Quick")]
assets = []
graphs = []
blend_nodes = []
with tempfile.TemporaryDirectory(prefix="als-camera-inputs-") as temporary:
    def export_text(obj):
        task = unreal.AssetExportTask()
        task.object, task.exporter = obj, unreal.ObjectExporterT3D()
        task.filename = str(Path(temporary) / "object.t3d")
        task.automated, task.prompt = True, False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Cannot export " + obj.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")

    for path in paths:
        obj = unreal.load_asset(path)
        if obj is None:
            raise RuntimeError("Missing camera asset " + path)
        item = {"path": obj.get_path_name(), "class": obj.get_class().get_path_name()}
        if not isinstance(obj, unreal.Blueprint):
            item["nativeText"] = export_text(obj)
        if isinstance(obj, unreal.CurveFloat):
            item["samples"] = [{"time": i / 100, "value": obj.get_float_value(i / 100)} for i in range(101)]
        assets.append(item)
    prefixes = tuple(asset["path"] + ":" for asset in assets)
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        if graph.get_path_name().startswith(prefixes) and graph.get_name() == "AnimGraph":
            graphs.append({"path": graph.get_path_name(), "nativeText": export_text(graph)})
    camera_graph = root + "AB_Als_Camera.AB_Als_Camera:AnimGraph."
    for node in unreal.ObjectIterator(unreal.EdGraphNode):
        if node.get_path_name().startswith(camera_graph) and node.get_class().get_name() in (
                "AlsAnimGraphNode_GameplayTagsBlend", "AnimGraphNode_BlendListByBool"):
            blend_nodes.append({"path": node.get_path_name(),
                "blendType": node.get_editor_property("node").get_editor_property("blend_type").name})
if not graphs:
    raise RuntimeError("Camera animation graphs missing")
settings = unreal.load_asset(root + "Data/CS_Als_Default")
first = settings.get_editor_property("first_person")
third = settings.get_editor_property("third_person")
offset = third.get_editor_property("trace_override_offset")
values = {
    "ignoreTimeDilation": settings.get_editor_property("ignore_time_dilation"),
    "teleportDistanceThreshold": settings.get_editor_property("teleport_distance_threshold"),
    "firstPerson": {"fieldOfView": first.get_editor_property("field_of_view"),
                    "cameraSocketName": str(first.get_editor_property("camera_socket_name"))},
    "thirdPerson": {name: third.get_editor_property(prop) for name, prop in (
        ("fieldOfView", "field_of_view"), ("traceRadius", "trace_radius"),
        ("enableTraceDistanceSmoothing", "enable_trace_distance_smoothing"))},
}
for name, prop in (("firstPivotSocketName", "first_pivot_socket_name"),
                   ("secondPivotSocketName", "second_pivot_socket_name"),
                   ("traceShoulderLeftSocketName", "trace_shoulder_left_socket_name"),
                   ("traceShoulderRightSocketName", "trace_shoulder_right_socket_name")):
    values["thirdPerson"][name] = str(third.get_editor_property(prop))
values["thirdPerson"]["traceChannel"] = third.get_editor_property("trace_channel").value
values["thirdPerson"]["traceOverrideOffset"] = [offset.x, offset.y, offset.z]
values["thirdPerson"]["traceDistanceSmoothingHalfLife"] = third.get_editor_property(
    "trace_distance_smoothing").get_editor_property("interpolation_half_life")
component = unreal.get_default_object(unreal.load_class(None, root + "B_Als_CameraComponent.B_Als_CameraComponent_C"))
if component.get_editor_property("settings") != settings:
    raise RuntimeError("Camera component no longer uses the exported settings")
reference = []
def f32(value):
    return struct.unpack("f", struct.pack("f", value))[0]
def rotation_values(value):
    return [value.pitch, value.yaw, value.roll]
for hz in (30, 60, 120):
    for half_life in (0.0, 0.05, 0.2):
        current = unreal.Rotator(pitch=10.0, yaw=170.0, roll=-5.0)
        frames = []
        for frame in range(hz):
            target = unreal.Rotator(pitch=-20.0 if frame < hz // 2 else 35.0,
                yaw=-170.0 if frame < hz // 2 else 5.0, roll=0.0)
            current = unreal.AlsRotation.damper_exact_rotation(current, target, 1 / hz, half_life)
            frames.append({"target": rotation_values(target), "rotation": rotation_values(current)})
        reference.append({"hz": hz, "halfLife": f32(half_life), "delta": f32(1 / hz),
            "alpha": unreal.AlsMath.damper_exact_alpha(1 / hz, half_life), "frames": frames})
rotation_boundaries = []
for half_life in (0.0, 0.2):
    for delta in (0.0, 1 / 60):
        for initial_yaw in (0.0, 180.0, -180.0, 540.0):
            for yaw in (-180.0, 174.999, 175.0, 175.001, 180.0, 360.0, 0.00001):
                current, target = unreal.Rotator(yaw=initial_yaw), unreal.Rotator(yaw=yaw)
                rotation_boundaries.append({"current": rotation_values(current), "target": rotation_values(target),
                    "delta": f32(delta), "halfLife": f32(half_life),
                    "rotation": rotation_values(unreal.AlsRotation.damper_exact_rotation(current, target, delta, half_life))})
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "assets": assets, "settings": values,
    "blendNodes": sorted(blend_nodes, key=lambda n: n["path"]),
    "rotationReference": reference, "rotationBoundaries": rotation_boundaries,
    "graphs": sorted(graphs, key=lambda g: g["path"])}, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_CAMERA_INPUTS_OK assets=" + str(len(assets)) + " graphs=" + str(len(graphs)) + " assets_saved=0")

"""Record actual UE camera ticks; ALS_CAMERA_COMPONENT_BASE=1 adds moving bases.

Writes only the requested reference JSON; never saves source assets.
"""
import os
import json
import hashlib
from pathlib import Path
import unreal

output = Path(os.environ["ALS_CAMERA_COMPONENT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.AlsAnimationGraphLibrary.export_camera_component_trace(str(output)):
    raise RuntimeError("Camera component export failed")
data = json.loads(output.read_text(encoding="utf-8-sig"))
if [t["hz"] for t in data["traces"]] != [30, 60, 120] or any(len(t["frames"]) != t["hz"] * 4 for t in data["traces"]):
    raise RuntimeError("Incomplete component traces")
data["cameraClass"] = "/ALS/ALSCamera/B_Als_CameraComponent.B_Als_CameraComponent_C"
data["characterClass"] = "/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"
settings_bytes = (Path(__file__).resolve().parents[2] / "assets/config/refactored_camera_inputs.json").read_bytes()
expected = json.loads(settings_bytes)["settings"]
settings = unreal.get_default_object(unreal.load_class(None, data["cameraClass"])).get_editor_property("settings")
first, third = settings.get_editor_property("first_person"), settings.get_editor_property("third_person")
if (settings.get_editor_property("teleport_distance_threshold") != expected["teleportDistanceThreshold"] or
        first.get_editor_property("field_of_view") != expected["firstPerson"]["fieldOfView"] or
        third.get_editor_property("field_of_view") != expected["thirdPerson"]["fieldOfView"] or
        third.get_editor_property("trace_radius") != expected["thirdPerson"]["traceRadius"] or
        third.get_editor_property("enable_trace_distance_smoothing") != expected["thirdPerson"]["enableTraceDistanceSmoothing"] or
        third.get_editor_property("trace_distance_smoothing").get_editor_property("interpolation_half_life") != expected["thirdPerson"]["traceDistanceSmoothingHalfLife"]):
    raise RuntimeError("Native component settings differ from replay settings")
data["settingsDigest"] = hashlib.sha256(settings_bytes).hexdigest()
data["engine"] = unreal.SystemLibrary.get_engine_version()
output.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
unreal.log("ALS_CAMERA_COMPONENT_EXPORT_OK")
if os.environ.get("ALS_CAMERA_COMPONENT_QUIT") == "1":
    _ticks = 0
    def _quit_after_tick(delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_quit_handle)
            unreal.SystemLibrary.quit_editor()
    _quit_handle = unreal.register_slate_post_tick_callback(_quit_after_tick)

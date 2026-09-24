"""Export actual Mantling RootMotionSource traces on transient actors."""
import json
import hashlib
import os
from pathlib import Path
import unreal

output=Path(os.environ["ALS_MANTLE_MOTION_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
output.parent.mkdir(parents=True,exist_ok=True)
blend_output=Path(os.environ["ALS_MANTLE_BLEND_OUTPUT"])
if not blend_output.is_absolute() or blend_output==output:
    raise ValueError("Distinct absolute production blend output required")
inputs=json.loads(Path(__file__).parents[2].joinpath("assets/config/refactored_mantle_inputs.json").read_text(encoding="utf-8"))
blends=[]
for row in inputs["montages"]:
    montage=unreal.load_asset(row["path"])
    blend=montage.get_blend_in_args()
    if blend.get_editor_property("custom_curve") is not None:
        raise RuntimeError("Unsupported custom mantle blend curve")
    blends.append({"path":montage.get_path_name(),"time":blend.get_editor_property("blend_time"),
                   "option":blend.get_editor_property("blend_option").name})
blend_output.parent.mkdir(parents=True,exist_ok=True)
blend_output.write_text(json.dumps({"schemaVersion":1,"montages":blends},indent=2)+"\n",encoding="utf-8",newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_mantling_root_motion_trace(str(output)):
    raise RuntimeError("Native mantle root motion export failed")
data=json.loads(output.read_text(encoding="utf-8-sig"))
if len(data["traces"])!=63 or any(len(t["frames"])!=t["hz"]+2 for t in data["traces"]):
    raise RuntimeError("Incomplete mantle traces")
config=Path(__file__).parents[2]/"assets/config"
data["settingsSha256"]=hashlib.sha256((config/"refactored_mantle_inputs.json").read_bytes()).hexdigest()
data["rootsSha256"]=hashlib.sha256((config/"refactored_mantle_root_tracks.json").read_bytes()).hexdigest()
data["blendsSha256"]=hashlib.sha256(blend_output.read_bytes()).hexdigest()
data["engine"]=unreal.SystemLibrary.get_engine_version()
output.write_text(json.dumps(data,separators=(",",":"),allow_nan=False),encoding="utf-8",newline="\n")
unreal.log("ALS_MANTLE_MOTION_OK traces=63 assets_saved=0")
if os.environ.get("ALS_MANTLE_MOTION_QUIT")=="1":
    _ticks=0
    def quit_after_tick(delta):
        global _ticks
        _ticks+=1
        if _ticks>=3:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle=unreal.register_slate_post_tick_callback(quit_after_tick)

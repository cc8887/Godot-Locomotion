"""Native Montage-only motion owner and extraction trace; no asset writes."""
import os
from pathlib import Path
import unreal

output = os.environ["ALS_ROOT_MOTION_OUTPUT"]
if not Path(output).is_absolute():
    raise ValueError("ALS_ROOT_MOTION_OUTPUT must be absolute")
if not unreal.AlsMontageLifecycleProbe.export_trace(output, True, True):
    raise RuntimeError("Native montage root motion trace failed")
unreal.log("ALS_ROOT_MOTION_NATIVE_OK assets_saved=0")
if os.environ.get("ALS_ROOT_MOTION_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

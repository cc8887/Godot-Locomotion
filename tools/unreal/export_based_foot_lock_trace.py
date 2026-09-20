"""Run the compiled Refactored foot functions on captured Godot frame inputs."""
import os
import time
from pathlib import Path
import unreal

request = Path(os.environ["ALS_BASED_FOOT_REQUEST"])
output = Path(os.environ["ALS_BASED_FOOT_OUTPUT"])
if not request.is_absolute() or not output.is_absolute() or request == output:
    raise ValueError("Distinct absolute based-foot request/output paths are required")
if not unreal.AlsAnimationGraphLibrary.export_based_foot_lock_trace(str(request), str(output)):
    raise RuntimeError("Native based-foot export failed")
unreal.log(f"ALS_BASED_FOOT_REPLAY_OK output={output} assets_saved=0")
if os.environ.get("ALS_BASED_FOOT_QUIT") == "1":
    _deadline = time.monotonic() + 10
    def _finish(_delta):
        if time.monotonic() >= _deadline:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)

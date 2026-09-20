"""Run actual Refactored foot rig units on transient hierarchies; no assets saved."""
import json
import os
import time
from pathlib import Path
import unreal

output = Path(os.environ["ALS_REFACTORED_FOOT_CONTROLS_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute output path is required")
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.AlsAnimationGraphLibrary.export_refactored_foot_controls(str(output)):
    raise RuntimeError("Native foot control probe failed")
payload = json.loads(output.read_text(encoding="utf-8"))
assert payload["schemaVersion"] == 1
assert len(payload["cases"]) == 12
assert all(len(case["frames"]) == 120 for case in payload["cases"])
unreal.log("ALS_REFACTORED_FOOT_CONTROLS_OK cases=12 frames=1440 assets_saved=0")
if os.environ.get("ALS_REFACTORED_FOOT_CONTROLS_QUIT") == "1":
    # Leave startup's Python execution callback before requesting shutdown.
    # Immediate quit can tear down the Editor while startup work is still active.
    _quit_deadline = time.monotonic() + 10
    def _finish_editor(_delta):
        if time.monotonic() < _quit_deadline:
            return
        unreal.unregister_slate_post_tick_callback(_quit_handle)
        unreal.SystemLibrary.quit_editor()
    _quit_handle = unreal.register_slate_post_tick_callback(_finish_editor)

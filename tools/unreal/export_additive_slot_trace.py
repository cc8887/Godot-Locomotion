"""Export native dynamic additive Slot poses without changing or saving assets."""
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_ADDITIVE_SLOT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_ADDITIVE_SLOT_OUTPUT must be absolute")
if not unreal.AlsMontageLifecycleProbe.export_additive_trace(str(output)):
    raise RuntimeError("Native additive Slot export failed")
if os.environ.get("ALS_ADDITIVE_SLOT_QUIT") == "1":
    # Return from startup script execution before requesting normal shutdown.
    _quit_ticks = 0

    def _quit_after_tick(delta):
        global _quit_ticks
        _quit_ticks += 1
        if _quit_ticks >= 30:
            unreal.unregister_slate_post_tick_callback(_quit_handle)
            unreal.SystemLibrary.quit_editor()

    _quit_handle = unreal.register_slate_post_tick_callback(_quit_after_tick)

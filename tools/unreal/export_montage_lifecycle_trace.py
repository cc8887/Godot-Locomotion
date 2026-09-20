"""Run the native Montage probe in an ordinary Editor process without saving assets."""
import os
import json
from pathlib import Path

import unreal


output = Path(os.environ["ALS_MONTAGE_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_MONTAGE_TRACE_OUTPUT must be absolute")
if not unreal.AlsMontageLifecycleProbe.export_trace(str(output), os.environ.get("ALS_MONTAGE_TRACE_ACTIONS") == "1"):
    raise RuntimeError("Native montage trace failed")
if os.environ.get("ALS_MONTAGE_BINDINGS_OUTPUT"):
    bindings = Path(os.environ["ALS_MONTAGE_BINDINGS_OUTPUT"])
    if not bindings.is_absolute():
        raise ValueError("ALS_MONTAGE_BINDINGS_OUTPUT must be absolute")
    trace = json.loads(output.read_text(encoding="utf-8"))
    if trace["schemaVersion"] != 2:
        raise ValueError("Authored montage bindings require the actions probe")
    bindings.write_text(json.dumps({"schemaVersion": 1, "assets": trace["assets"][8:]}, indent=2) + "\n", encoding="utf-8")
if os.environ.get("ALS_MONTAGE_TRACE_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

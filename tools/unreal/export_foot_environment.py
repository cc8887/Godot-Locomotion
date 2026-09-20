"""Actual pelvis spring and foot traces against transient UE collision geometry."""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_FOOT_ENVIRONMENT_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute environment output is required")
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.AlsAnimationGraphLibrary.export_foot_environment(str(output)):
    raise RuntimeError("Native environment probe failed")
payload = json.loads(output.read_text(encoding="utf-8"))
assert payload["schemaVersion"] == 1
assert len(payload["springs"]) == 12
assert all(len(case["frames"]) == 120 for case in payload["springs"])
assert len(payload["traces"]) == 48
assert sum(row["blocking"] for row in payload["traces"]) >= 24
assert any(row["offsetZ"] != 0 for row in payload["traces"])
unreal.log("ALS_FOOT_ENVIRONMENT_OK springs=1440 traces=48 assets_saved=0")
# Commandlet and ExecutePythonScript each own their normal exit lifecycle.

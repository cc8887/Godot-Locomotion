"""Read-only UE per-item IK oracle; includes weighted hierarchy propagation."""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_RIG_IK_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute rig IK output is required")
output.parent.mkdir(parents=True, exist_ok=True)
if not unreal.AlsAnimationGraphLibrary.export_rig_two_bone_ik(str(output)):
    raise RuntimeError("Native rig IK export failed")
payload = json.loads(output.read_text(encoding="utf-8"))
assert payload["schemaVersion"] == 1
assert len(payload["rows"]) == 504
assert all(len(row["after"]) == 6 for row in payload["rows"])
unreal.log("ALS_RIG_IK_OK cases=504 bones=3024 assets_saved=0")
# ExecutePythonScript handles normal Editor exit after this script completes.
# Do not request an additional immediate quit from the startup callback.

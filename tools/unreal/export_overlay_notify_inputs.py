"""Read all Overlay sequence notify identities with the established native reader."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path

import unreal


root = Path(os.environ["ALS_OVERLAY_REPOSITORY"])
output = Path(os.environ["ALS_OVERLAY_NOTIFY_OUTPUT"])
if not root.is_absolute() or not output.is_absolute():
    raise ValueError("Absolute repository/output paths required")
spec = importlib.util.spec_from_file_location("als_native_notify_reader", Path(__file__).with_name("export_action_notify_inputs.py"))
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
native_bytes = (root / "assets/config/v4_overlay_inputs.json").read_bytes()
native = json.loads(native_bytes)
assets = [reader.read_asset(entry["source"]) for entry in sorted(native["assets"], key=lambda entry: entry["source"])]
if len(assets) != 29:
    raise RuntimeError("Incomplete Overlay source closure")
payload = dict(schemaVersion=1, source=native["source"], overlaySha256=hashlib.sha256(native_bytes).hexdigest(),
               notifySchemaVersion=1, syncAssets=assets)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_OVERLAY_NOTIFY_INPUTS_OK assets=29 notifies=%d assets_saved=0" % sum(len(a["notifies"]) for a in assets))
if os.environ.get("ALS_OVERLAY_NOTIFY_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

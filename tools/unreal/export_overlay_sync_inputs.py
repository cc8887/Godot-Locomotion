"""Read immutable timing/marker metadata for every original Overlay source asset."""
import hashlib
import json
import math
import os
from pathlib import Path

import unreal


root = Path(os.environ["ALS_OVERLAY_REPOSITORY"])
output = Path(os.environ["ALS_OVERLAY_SYNC_OUTPUT"])
if not root.is_absolute() or not output.is_absolute():
    raise ValueError("Absolute repository/output paths required")
native_bytes = (root / "assets/config/v4_overlay_inputs.json").read_bytes()
native = json.loads(native_bytes)
assets = []
for entry in sorted(native["assets"], key=lambda item: item["source"]):
    asset = unreal.load_asset(entry["source"])
    if not isinstance(asset, unreal.AnimSequence) or asset.get_path_name() != entry["source"]:
        raise RuntimeError("Missing or foreign Overlay sequence")
    rate = asset.get_editor_property("rate_scale")
    length = asset.get_play_length()
    if not math.isfinite(rate) or length != entry["evaluation"]["sequencePlayLength"]:
        raise RuntimeError("Overlay timing metadata changed")
    markers = [dict(index=index, name=str(marker.get_editor_property("marker_name")),
                    time=marker.get_editor_property("time"))
               for index, marker in enumerate(unreal.AnimationLibrary.get_animation_sync_markers(asset))]
    assets.append(dict(path=asset.get_path_name(), length=length, rateScale=rate, markers=markers))
if len(assets) != 29:
    raise RuntimeError("Incomplete Overlay asset closure")
payload = dict(schemaVersion=1, source=native["source"],
               overlaySha256=hashlib.sha256(native_bytes).hexdigest(), assets=assets)
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_OVERLAY_SYNC_INPUTS_OK assets=29 markers=%d assets_saved=0" % sum(len(a["markers"]) for a in assets))
if os.environ.get("ALS_OVERLAY_SYNC_QUIT") == "1":
    unreal.SystemLibrary.quit_editor()

import json
import os
import unreal

output = os.environ["ALS_LEG_RIG_OUTPUT"]
assert os.path.isabs(output) and not os.path.exists(output)
assert unreal.AlsAnimationGraphLibrary.export_leg_rig(output)
with open(output, encoding="utf-8") as source:
    data = json.load(source)
assert len(data["cases"]) == 12
assert all(len(case["frames"]) == 120 for case in data["cases"])
unreal.log("ALS_LEG_RIG_OK cases=12 frames=1440 assets_saved=0")

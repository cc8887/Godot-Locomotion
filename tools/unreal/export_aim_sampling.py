"""Export real ALS Aim BlendSpace settings and native sampling, without saving assets."""
import json
import os
from pathlib import Path

import unreal


def export():
    output = Path(os.environ["ALS_AIM_SAMPLING_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_AIM_SAMPLING_OUTPUT must be absolute")
    output.parent.mkdir(parents=True, exist_ok=True)
    if not unreal.AlsAnimationGraphLibrary.export_aim_sampling(str(output)):
        raise RuntimeError("Native ALS Aim sampling export failed")
    value = json.loads(output.read_text(encoding="utf-8-sig"))
    if len(value["samples"]) != 3 or len(value["gridSamples"]) != 5:
        raise RuntimeError("Incomplete Aim BlendSpace")
    unreal.log("ALS_AIM_SAMPLING_EXPORT_OK samples=3 grid=5 static=" + str(len(value["staticSamples"])) +
               " frames=" + str(sum(len(run["frames"]) for run in value["runs"])) + " assets_saved=0")


export()

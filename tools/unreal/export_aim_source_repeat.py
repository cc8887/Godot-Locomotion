"""Repeat Aim native sampling and source-pose oracles inside a normal Editor."""
import os
import runpy
from pathlib import Path

import unreal


def export():
    output = Path(os.environ["ALS_AIM_REPEAT_DIR"])
    if not output.is_absolute():
        raise ValueError("ALS_AIM_REPEAT_DIR must be absolute")
    output.mkdir(parents=True, exist_ok=True)
    scripts = Path(__file__).resolve().parent
    os.environ["ALS_AIM_SAMPLING_OUTPUT"] = str(output / "aim_sampling.json")
    runpy.run_path(str(scripts / "export_aim_sampling.py"))
    os.environ["ALS_MOVEMENT_SOURCE_INDEX"] = str(output / "v4_aim_source_inputs.json")
    os.environ["ALS_MOVEMENT_SOURCE_ORACLE"] = str(output / "raw_pose_oracle.json")
    runpy.run_path(str(scripts / "export_movement_source_sequences.py"))
    os.environ["ALS_ADDITIVE_SOURCE_INDEX"] = os.environ["ALS_MOVEMENT_SOURCE_INDEX"]
    os.environ["ALS_ADDITIVE_SOURCE_ORACLE"] = str(output / "additive_pose_oracle.json")
    runpy.run_path(str(scripts / "export_additive_source_poses.py"))
    unreal.log("ALS_AIM_SOURCE_EDITOR_REPEAT_OK assets_saved=0")


export()

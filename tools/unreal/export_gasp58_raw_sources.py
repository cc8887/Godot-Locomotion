"""Export GASP58's ALS V4 source sequences for the current Godot requests."""

import os
import json
import runpy
from pathlib import Path

import unreal


def absolute_directory(name):
    path = Path(os.environ[name])
    if not path.is_absolute() or not path.is_dir():
        raise ValueError(name + " must name an existing absolute directory")
    return path


requests = absolute_directory("ALS_GASP_RAW_REQUEST_DIR")
output = absolute_directory("ALS_GASP_RAW_OUTPUT_DIR")
oracles = absolute_directory("ALS_GASP_RAW_ORACLE_DIR")
exporter = Path(__file__).resolve().parent / "export_movement_source_sequences.py"

for name in ("recovery_movement", "aim", "overlay", "ragdoll", "stop", "overlay_prop"):
    request = requests / (name + "-request.json")
    if not request.is_file():
        raise RuntimeError("Missing current Godot source request: " + str(request))
    os.environ["ALS_MOVEMENT_SOURCE_REQUEST"] = str(request)
    os.environ["ALS_MOVEMENT_SOURCE_INDEX"] = str(output / ("v4_" + name + "_source_inputs.json"))
    os.environ["ALS_MOVEMENT_SOURCE_ORACLE"] = str(oracles / (name + "-native-poses.json"))
    os.environ["ALS_RAW_SOURCE_PRESERVE_EXISTING"] = "1"
    runpy.run_path(str(exporter), run_name="__main__")
    index = json.loads(Path(os.environ["ALS_MOVEMENT_SOURCE_INDEX"]).read_bytes())
    for entry in index["assets"]:
        source = json.loads((output / entry["file"]).read_bytes())
        if not source["tracks"]:
            raise RuntimeError("Empty source tracks for " + entry["source"] +
                               "; run with -DisablePlugins=AnimationData")
    unreal.log("GODOT_ALS_GASP58_RAW_GROUP_OK name=" + name)

unreal.log("GODOT_ALS_GASP58_RAW_EXPORT_OK indices=6")

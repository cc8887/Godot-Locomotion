"""Export Kimodo-regenerated ALS V4 raw movement sources for the six Godot domains."""

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


requests = absolute_directory("KIMODO_RAW_REQUEST_DIR")
output = absolute_directory("KIMODO_RAW_OUTPUT_DIR")
config_directory = absolute_directory("KIMODO_ORIGINAL_RAW_DIR")
manifest_path = Path(os.environ["KIMODO_MANIFEST_PATH"])
if not manifest_path.is_absolute() or not manifest_path.is_file():
    raise ValueError("KIMODO_MANIFEST_PATH must name an existing absolute file")
exporter = Path(__file__).resolve().parent / "export_kimodo_raw_sources.py"

for name in ("recovery_movement", "aim", "overlay", "ragdoll", "stop", "overlay_prop"):
    request = requests / (name + "-request.json")
    if not request.is_file():
        raise RuntimeError("Missing Kimodo source request: " + str(request))
    os.environ["ALS_MOVEMENT_SOURCE_REQUEST"] = str(request)
    os.environ["ALS_MOVEMENT_SOURCE_INDEX"] = str(output / ("v4_" + name + "_source_inputs.json"))
    os.environ["KIMODO_ORIGINAL_RAW_DIR"] = str(config_directory)
    os.environ["KIMODO_MANIFEST_PATH"] = str(manifest_path)
    os.environ["ALS_RAW_SOURCE_PRESERVE_EXISTING"] = "1"
    runpy.run_path(str(exporter), run_name="__main__")
    index = json.loads(Path(os.environ["ALS_MOVEMENT_SOURCE_INDEX"]).read_bytes())
    for entry in index["assets"]:
        source = json.loads((output / entry["file"]).read_bytes())
        if not source["tracks"]:
            raise RuntimeError("Empty source tracks for " + entry["source"])
        if not source["evaluation"]:
            raise RuntimeError("Missing evaluation for " + entry["source"])
    unreal.log("GODOT_ALS_KIMODO_RAW_GROUP_OK name=" + name + " assets=" + str(len(index["assets"])))

unreal.log("GODOT_ALS_KIMODO_RAW_EXPORT_OK indices=6")

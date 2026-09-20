"""Read jump defaults and native range results without saving any UE asset."""
import json
import os
from pathlib import Path

import unreal

source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"
output = Path(os.environ["ALS_JUMP_INPUT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_JUMP_INPUT_OUTPUT must be absolute")
defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
payload = {
    "schemaVersion": 1,
    "source": source,
    "defaults": {name: defaults.get_editor_property(name) for name in ("Jumped", "JumpPlayRate", "Speed")},
    "mapRangeCases": [{"speedCm": speed, "playRate": unreal.MathLibrary.map_range_clamped(speed, 0, 600, 1.2, 1.5)}
                      for speed in (0, 75, 150, 300, 350, 600, 900)],
}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
character = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
graph = unreal.BlueprintLispPythonBridge.export_graph_to_text(character, "EventGraph")
if not graph.success:
    raise RuntimeError("Character event graph read failed: " + graph.message)
evidence = Path(os.environ["ALS_JUMP_CHARACTER_GRAPH_OUTPUT"])
if not evidence.is_absolute():
    raise ValueError("ALS_JUMP_CHARACTER_GRAPH_OUTPUT must be absolute")
evidence.write_text(graph.dsl_text, encoding="utf-8")
unreal.log("ALS_JUMP_EVENT_INPUTS_OK defaults=3 native_range_cases=7 assets_saved=0")
if os.environ.get("ALS_JUMP_QUIT_EDITOR") == "1":
    unreal.SystemLibrary.quit_editor()

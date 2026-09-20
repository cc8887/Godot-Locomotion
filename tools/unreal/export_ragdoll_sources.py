"""Export the exact ALS_Flail raw resource with the shared exporter and independent UE poses."""
import hashlib
import json
import os
import runpy
from pathlib import Path


def export():
    repository = Path(os.environ["ALS_RAGDOLL_REPOSITORY"])
    if not repository.is_absolute():
        raise ValueError("Absolute Ragdoll repository required")
    source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/ALS_Flail.ALS_Flail"
    player = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:AnimGraph.AnimGraphNode_StateMachine_10.Ragdoll States.AnimStateNode_0.In Ragdoll.AnimGraphNode_SequencePlayer_0"
    stable_id = hashlib.sha1(source.encode("utf-8")).hexdigest()
    base_index = json.loads((repository / "assets/config/v4_movement_source_inputs.json").read_text(encoding="utf-8-sig"))
    request = {"definitionDigest": base_index["request"]["definitionDigest"],
               "bindingDigest": hashlib.sha256((player + "|" + stable_id).encode("utf-8")).hexdigest().upper(),
               "players": 1, "samples": 1, "rootAssets": [{"assetId": stable_id, "source": source}]}
    request_path = Path(os.environ["ALS_MOVEMENT_SOURCE_REQUEST"])
    if not request_path.is_absolute():
        raise ValueError("Absolute source request required")
    request_path.parent.mkdir(parents=True, exist_ok=True)
    request_path.write_text(json.dumps(request, indent=2) + "\n", encoding="utf-8")
    os.environ["ALS_RAW_SOURCE_PRESERVE_EXISTING"] = "1"
    runpy.run_path(str(repository / "tools/unreal/export_movement_source_sequences.py"), run_name="__main__")


export()

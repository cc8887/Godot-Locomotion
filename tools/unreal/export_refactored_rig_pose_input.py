"""Sample actual GetControlRigInput on a transient instance; no asset/CDO edits."""
import itertools
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_RIG_POSE_INPUT_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute output is required")
mesh = unreal.new_object(unreal.SkeletalMeshComponent)
instance = unreal.new_object(unreal.AlsAnimationInstance, outer=mesh)
rows = []
for grounded, airborne, prediction in itertools.product(
    [-.4, 0, .2, .5, .9, 1, 1.2], [-.2, 0, .3, .75, 1, 1.4], [-.5, 0, .2, .5, .9, 1, 1.5]
):
    pose = instance.get_editor_property("pose_state")
    pose.set_editor_property("grounded_amount", grounded)
    pose.set_editor_property("InAirAmount", airborne)
    # The struct wrapper refers to the transient instance's nested storage.
    # Replacing the parent BlueprintReadOnly property is not permitted.
    assert instance.get_editor_property("pose_state").get_editor_property("grounded_amount") == pose.get_editor_property("grounded_amount")
    assert instance.get_editor_property("pose_state").get_editor_property("InAirAmount") == pose.get_editor_property("InAirAmount")
    air = instance.get_editor_property("InAirState")
    air.set_editor_property("ground_prediction_amount", prediction)
    assert instance.get_editor_property("InAirState").get_editor_property("ground_prediction_amount") == air.get_editor_property("ground_prediction_amount")
    rig = instance.get_control_rig_input()
    rows.append(dict(grounded=pose.get_editor_property("grounded_amount"),
                     airborne=pose.get_editor_property("InAirAmount"), prediction=air.get_editor_property("ground_prediction_amount"),
                     result=rig.get_editor_property("pelvis_offset_amount")))
assert len(rows) == 294
output.write_text(json.dumps(dict(schemaVersion=1, source="Actual UAlsAnimationInstance::GetControlRigInput; transient controlled PoseState/InAirState", rows=rows), indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log("ALS_RIG_POSE_INPUT_OK samples=294 assets_saved=0")

"""Run isolated native joint physics steps; save no UE assets."""
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_JOINT_SOLVER_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("ALS_JOINT_SOLVER_OUTPUT must be a new absolute output file")
if not unreal.AlsAnimationGraphLibrary.export_physics_joint_solver_reference(str(output)):
    raise RuntimeError("Native joint solver reference export failed")

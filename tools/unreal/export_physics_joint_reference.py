"""Read live Chaos joint settings and native angular utilities; save no UE assets."""
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_JOINT_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("ALS_JOINT_OUTPUT must be a new absolute output file")
if not unreal.AlsAnimationGraphLibrary.export_physics_joint_reference(str(output)):
    raise RuntimeError("Native joint reference export failed")
# Let the native -ExecutePythonScript runner defer Editor shutdown.

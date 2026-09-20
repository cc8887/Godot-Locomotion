"""Read native PhysicsAssets; transient bodies only, no UE assets saved."""
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_PHYSICS_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("ALS_PHYSICS_OUTPUT must be a new absolute output file")
if not unreal.AlsAnimationGraphLibrary.export_physics_assets(str(output)):
    raise RuntimeError("Native PhysicsAsset export failed")
# -ExecutePythonScript already defers QUIT_EDITOR until a complete editor tick
# after the script returns (FEditorPythonExecuter). Do not quit inside this script:
# that bypasses the native runner's required tick and can race editor teardown.

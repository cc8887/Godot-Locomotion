"""Read native initial leg transforms from transient imported mesh hierarchies."""
import json
import os
import time
from pathlib import Path
import unreal

output = Path(os.environ["ALS_FOOT_REFERENCE_OUTPUT"])
if not output.is_absolute() or output.exists():
    raise ValueError("A fresh absolute output is required")
mesh_path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"
skeleton_path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"
names = ["pelvis", "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r"]

def pose(transform):
    p = transform.translation
    q = transform.rotation
    s = transform.scale3d
    return dict(position=[p.x, p.y, p.z], rotation=[q.x, q.y, q.z, q.w], scale=[s.x, s.y, s.z])

rows = []
for source in [mesh_path, skeleton_path]:
    hierarchy = unreal.new_object(unreal.RigHierarchy)
    controller = hierarchy.get_controller(True)
    keys = controller.import_bones_from_asset(source)
    by_name = {str(key.name).lower(): key for key in keys}
    if not all(name in by_name for name in names):
        raise RuntimeError(f"Incomplete native leg hierarchy: {source}")
    rows.append(dict(source=source, imported_bones=len(keys), bones=[
        dict(name=name, initial_global=pose(hierarchy.get_global_transform(by_name[name], True)))
        for name in names
    ]))

output.write_text(json.dumps(dict(schemaVersion=1,
    source="URigHierarchyController.ImportBonesFromAsset + GetGlobalTransform(bInitial=true); transient; no asset saves",
    cases=rows), indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log(f"ALS_FOOT_REFERENCE_OK cases={len(rows)} bones={len(names)} assets_saved=0 output={output}")
if os.environ.get("ALS_FOOT_REFERENCE_QUIT") == "1":
    _deadline = time.monotonic() + 10
    def _finish(_delta):
        if time.monotonic() >= _deadline:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)

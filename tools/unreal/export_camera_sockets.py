"""Read effective camera sockets from the actual Refactored character mesh."""
import json
import os
from pathlib import Path
import unreal

output = Path(os.environ["ALS_CAMERA_SOCKETS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
character = unreal.get_default_object(unreal.load_class(None, "/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"))
component = character.get_editor_property("mesh")
mesh = component.get_skeletal_mesh_asset()
skeleton = mesh.get_editor_property("skeleton")
names = ("FirstPersonCamera", "ThirdPersonTraceShoulderLeft", "ThirdPersonTraceShoulderRight")
candidates = {}
for socket in unreal.ObjectIterator(unreal.SkeletalMeshSocket):
    owner = socket.get_outer()
    if owner not in (mesh, skeleton):
        continue
    name = str(socket.get_editor_property("socket_name"))
    if name not in names:
        continue
    # Mesh sockets override skeleton sockets with the same FName.
    if name in candidates and candidates[name].get_outer() == mesh:
        continue
    candidates[name] = socket
if set(candidates) != set(names):
    raise RuntimeError("Missing camera sockets: " + str(set(names) - set(candidates)))
rows = []
for name in names:
    socket = candidates[name]
    location = socket.get_editor_property("relative_location")
    rotation = socket.get_editor_property("relative_rotation").quaternion()
    scale = socket.get_editor_property("relative_scale")
    rows.append(dict(name=name, source=socket.get_path_name(), bone=str(socket.get_editor_property("bone_name")),
        translation=[location.x, location.y, location.z], rotation=[rotation.x, rotation.y, rotation.z, rotation.w],
        scale=[scale.x, scale.y, scale.z]))
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(dict(schemaVersion=1, character=character.get_class().get_path_name(),
    mesh=mesh.get_path_name(), skeleton=skeleton.get_path_name(), sockets=rows), indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log("ALS_CAMERA_SOCKETS_OK count=3 assets_saved=0")

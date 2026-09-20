"""Read ALS V4 movement assets and execute the engine's real CalcVelocity; never save assets."""
import json
import os
import re
import tempfile
from pathlib import Path

import unreal


def native_text(obj):
    with tempfile.TemporaryDirectory(prefix="als-movement-") as directory:
        task = unreal.AssetExportTask()
        task.object = obj
        task.exporter = unreal.ObjectExporterT3D()
        task.filename = str(Path(directory) / "object.t3d")
        task.automated = True
        task.prompt = False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native export failed: " + obj.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


def vector(value):
    return [value.x, value.y, value.z]


output = Path(os.environ["ALS_CHARACTER_MOVEMENT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_CHARACTER_MOVEMENT_OUTPUT must be absolute")
source = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"
defaults = unreal.get_default_object(unreal.load_class(None, source + "_C"))
movement = defaults.get_editor_property("CharacterMovement")
model = defaults.get_editor_property("MovementModel")
table = model.get_editor_property("DataTable")
rows = json.loads(unreal.DataTableFunctionLibrary.export_data_table_to_json_string(table))
paths = set()


def find_curves(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if re.sub(r"[^a-z]", "", key.lower()).startswith("movementcurve") and item != "None":
                match = re.search(r"(/Game/[^']+)", item)
                if not match:
                    raise RuntimeError("Unsupported movement curve: " + item)
                paths.add(match.group(1))
            else:
                find_curves(item)
    elif isinstance(value, list):
        for item in value:
            find_curves(item)


find_curves(rows)
curves = []
for path in sorted(paths):
    curve = unreal.load_asset(path)
    if not isinstance(curve, unreal.CurveVector):
        raise RuntimeError("Missing vector movement curve: " + path)
    curves.append({"path": path, "nativeText": native_text(curve), "verification": [
        {"input": step / 100, "value": vector(curve.get_vector_value(step / 100))}
        for step in range(-50, 351)]})

properties = ("max_acceleration", "braking_deceleration_walking", "ground_friction", "braking_friction",
              "braking_friction_factor", "use_separate_braking_friction", "braking_sub_step_time",
              "min_analog_walk_speed", "max_walk_speed", "max_walk_speed_crouched", "air_control",
              "falling_lateral_friction", "braking_deceleration_falling")
component_defaults = {name: movement.get_editor_property(name) for name in properties}
graph_names = ("GetMappedSpeed", "GetTargetMovementSettings", "UpdateCharacterMovement",
               "UpdateDynamicMovementSettings", "GetAllowedGait", "CanSprint", "TickGraph", "SetEssentialValues")
graphs = []
for name in graph_names:
    graph = unreal.load_object(None, source + ":" + name)
    if graph is None:
        raise RuntimeError("Missing movement graph: " + name)
    graphs.append({"name": name, "path": graph.get_path_name(), "nativeText": native_text(graph)})

# An actual transient editor actor supplies HasValidData/CharacterOwner. No copied
# implementation, Blueprint replacement, mesh animation, or physics tick is used.
subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
actor = subsystem.spawn_actor_from_class(unreal.Character, unreal.Vector(0, 0, -100000), transient=True)
if actor is None:
    raise RuntimeError("Cannot create native movement probe")
cases = []
try:
    component = actor.get_editor_property("CharacterMovement")
    component.set_updated_component(actor.get_editor_property("CapsuleComponent"))
    component.set_movement_mode(unreal.MovementMode.MOVE_WALKING)
    for name in ("braking_friction_factor", "use_separate_braking_friction", "braking_sub_step_time",
                 "braking_friction", "min_analog_walk_speed"):
        if component.get_editor_property(name) != component_defaults[name]:
            component.set_editor_property(name, component_defaults[name])
    for delta in (1 / 120, 1 / 60, 1 / 30, .08, .2):
        for friction in (0, 1, 4, 8):
            for velocity in ((0, 0, 0), (100, 0, 0), (375, 0, 0), (800, 0, 0), (.005, 0, 0), (9, 0, 0)):
                for direction in ((0, 0, 0), (1, 0, 0), (-1, 0, 0), (0, 1, 0), (.3, .4, 0)):
                    amount = (sum(value * value for value in direction)) ** .5
                    component.set_editor_property("velocity", unreal.Vector(*velocity))
                    if not unreal.AlsAnimationGraphLibrary.set_movement_probe_input(
                            component, unreal.Vector(*(value * 1500 for value in direction)), amount):
                        raise RuntimeError("Native movement probe input rejected")
                    component.set_editor_property("max_acceleration", 1500)
                    component.set_editor_property("max_walk_speed", 375)
                    for factor, deceleration in ((0, 0), (0, 800), (1, 0), (1, 800), (2, 0), (2, 800)):
                        component.set_editor_property("velocity", unreal.Vector(*velocity))
                        component.set_editor_property("braking_friction_factor", factor)
                        component.calc_velocity(delta, friction, False, deceleration)
                        cases.append({"delta": delta, "friction": friction, "velocity": velocity,
                                      "acceleration": [value * 1500 for value in direction], "analog": amount,
                                      "maxSpeed": 375, "brakingDeceleration": deceleration, "brakingFrictionFactor": factor,
                                      "result": vector(component.get_editor_property("velocity"))})
finally:
    if not subsystem.destroy_actor(actor):
        raise RuntimeError("Failed to destroy transient movement probe")

payload = {"schemaVersion": 1, "source": source, "graphs": graphs,
           "movementModel": {"path": table.get_path_name(), "row": str(model.get_editor_property("RowName")), "rows": rows},
           "componentPath": movement.get_path_name(), "componentText": native_text(movement),
           "componentDefaults": component_defaults, "movementCurves": curves, "velocityVerification": cases}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_CHARACTER_MOVEMENT_INPUTS_OK curves=" + str(len(curves)) + " cases=" + str(len(cases)) + " assets_saved=0")

if os.environ.get("ALS_CHARACTER_MOVEMENT_QUIT") == "1":
    _ticks = 0
    _handle = None

    def _quit_later(_delta):
        global _ticks
        _ticks += 1
        if _ticks >= 3:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()

    _handle = unreal.register_slate_post_tick_callback(_quit_later)

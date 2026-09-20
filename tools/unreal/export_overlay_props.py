"""Read the native V4 held-object Blueprint and component templates; never save assets."""
import json
import hashlib
import os
import tempfile
from pathlib import Path

import unreal

SOURCE = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_AnimMan_CharacterBP.ALS_AnimMan_CharacterBP"
BOW = "/Game/AdvancedLocomotionV4/Props/Meshes/Bow_AnimBP.Bow_AnimBP"


def native_text(obj, directory):
    task = unreal.AssetExportTask()
    task.object, task.exporter = obj, unreal.ObjectExporterT3D()
    task.filename = str(directory / "object.t3d")
    task.automated, task.prompt = True, False
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Cannot export " + obj.get_path_name())
    data = Path(task.filename).read_bytes()
    return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")


output = Path(os.environ["ALS_OVERLAY_PROPS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output path required")
blueprint = unreal.load_asset(SOURCE)
unreal.load_asset(BOW)
generated = unreal.load_class(None, SOURCE + "_C")
if blueprint is None or generated is None:
    raise RuntimeError("Missing native held-object character")
with tempfile.TemporaryDirectory(prefix="als-overlay-props-") as temporary:
    directory = Path(temporary)
    graphs = []
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        if not any(graph.get_path_name().startswith(source + ":") for source in (SOURCE, BOW)):
            continue
        name = graph.get_name()
        if (graph.get_path_name() == BOW + ":AnimGraph" or
                graph.get_path_name() == SOURCE + ":" + name and name in (
                    "AttachToHand", "ClearHeldObject", "UpdateHeldObject", "UpdateHeldObjectAnimations",
                    "OnOverlayStateChanged", "RagdollStart", "RagdollEnd", "UserConstructionScript", "EventGraph")):
            graphs.append({"name": name, "path": graph.get_path_name(), "nativeText": native_text(graph, directory)})
    components, cases = [], []
    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actor = subsystem.spawn_actor_from_class(generated, unreal.Vector(0, 0, -100000), transient=True)
    if actor is None:
        raise RuntimeError("Cannot spawn transient held-object character")
    try:
        held = actor.get_editor_property("HeldObjectRoot")
        static = actor.get_editor_property("StaticMesh")
        skeletal = actor.get_editor_property("SkeletalMesh")
        for name, component in (("HeldObjectRoot", held), ("StaticMesh", static), ("SkeletalMesh", skeletal)):
            components.append({"name": name, "nativeText": native_text(component, directory)})
        system = unreal.get_default_object(unreal.SystemLibrary)
        for value in [*range(13), 0, 8, 5, 0]:
            system.call_method("SetBytePropertyByName", args=(actor, "OverlayState", value))
            overlay = actor.get_editor_property("OverlayState")
            if overlay.value != value:
                raise RuntimeError("Failed to assign Overlay value")
            actor.call_method("UpdateHeldObject")
            static_asset = static.get_editor_property("static_mesh")
            skeletal_asset = skeletal.get_editor_property("skinned_asset")
            location = held.get_editor_property("relative_location")
            rotation = held.get_editor_property("relative_rotation")
            scale = held.get_editor_property("relative_scale3d")
            anim_class = skeletal.get_editor_property("anim_class")
            cases.append({"overlayValue": value, "overlayName": str(overlay),
                          "staticMesh": static_asset.get_path_name() if static_asset else None,
                          "skeletalMesh": skeletal_asset.get_path_name() if skeletal_asset else None,
                          "animClass": anim_class.get_path_name() if anim_class else None,
                          "socket": str(held.get_attach_socket_name()),
                          "locationCm": [location.x, location.y, location.z],
                          "rotationDegrees": [rotation.pitch, rotation.yaw, rotation.roll],
                          "scale": [scale.x, scale.y, scale.z]})
    finally:
        subsystem.destroy_actor(actor)
    payload = {"schemaVersion": 1, "source": SOURCE,
               "graphs": sorted(graphs, key=lambda g: g["path"]),
               "components": sorted(components, key=lambda c: c["name"]),
               "cases": cases,
               "defaultsText": native_text(unreal.get_default_object(generated), directory)}
output.parent.mkdir(parents=True, exist_ok=True)
payload_bytes = (json.dumps(payload, indent=2, allow_nan=False) + "\n").encode("utf-8")
output.write_bytes(payload_bytes)

# Reuse the established exact DataModel source format. FBX is only the mesh;
# the bow's evaluator receives native original keys, not rebaked FBX curves.
decode = lambda text: json.loads(text, parse_int=lambda token: -0.0 if token == "-0" else int(token))
animation = unreal.load_asset("/Game/AdvancedLocomotionV4/Props/Meshes/Bow_Draw.Bow_Draw")
raw = decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(animation))
raw["evaluation"] = decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(animation))
skeleton = decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(animation.get_editor_property("skeleton")))
identifier = hashlib.sha1(raw["source"].encode("utf-8")).hexdigest()
raw_path = output.parent / "raw_sequences" / (identifier + ".json")
raw_bytes = (json.dumps(raw, indent=2, allow_nan=False) + "\n").encode("utf-8")
raw_path.parent.mkdir(parents=True, exist_ok=True)
if raw_path.exists() and raw_path.read_bytes() != raw_bytes:
    raise RuntimeError("Existing bow raw keys differ; preserve for investigation")
raw_path.write_bytes(raw_bytes)
definition = json.loads((Path(os.environ["ALS_OVERLAY_REPOSITORY"]) / "assets/config/v4_overlay_source_inputs.json").read_text(encoding="utf-8"))["request"]["definitionDigest"]
index = {"schemaVersion": 1, "source": "AnimDataModel.BoneAnimationTracks.InternalTrackData",
         "request": {"definitionDigest": definition, "bindingDigest": hashlib.sha256(payload_bytes).hexdigest(),
                     "players": 1, "samples": 1, "rootAssets": [{"assetId": identifier, "source": raw["source"]}]},
         "skeletons": [skeleton], "assets": [{"assetId": identifier, "source": raw["source"],
             "file": "raw_sequences/" + identifier + ".json", "sha256": hashlib.sha256(raw_bytes).hexdigest()}]}
(output.parent / "v4_overlay_prop_source_inputs.json").write_text(json.dumps(index, indent=2, allow_nan=False) + "\n", encoding="utf-8")
samples = [decode(unreal.AlsSourceAnimationLibrary.read_raw_animation_pose(animation, time, True, False, False))
           for time in (0, .03125, .125, .25, .4999, .5, .75, 1, 1.125)]
(output.parent / "v4_overlay_prop_oracle.json").write_text(json.dumps({"source": raw["source"], "samples": samples}, indent=2, allow_nan=False) + "\n", encoding="utf-8")
unreal.log("ALS_OVERLAY_PROPS_EXPORT_OK graphs=" + str(len(graphs)) + " components=" + str(len(components)) + " assets_saved=0")

"""Read Refactored mantle assets and native absolute montage root samples.

No source assets are saved. Selection cases exercise all thirteen authored
native overlay tags; the selection graph is retained for structural checking.
"""
import json
import math
import os
import struct
import tempfile
from pathlib import Path
import unreal

output = Path(os.environ["ALS_MANTLE_INPUTS_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
character_path = "/ALS/ALS/Character/B_Als_Character.B_Als_Character"
settings_root = "/ALS/ALS/Data/Character/Mantle"
assets, montages, graphs, cases = [], {}, [], []
unreal.AssetRegistryHelpers.get_asset_registry().scan_paths_synchronous([settings_root], force_rescan=True)

with tempfile.TemporaryDirectory(prefix="als-mantle-inputs-") as temporary:
    def native_text(obj):
        task = unreal.AssetExportTask()
        task.object, task.exporter = obj, unreal.ObjectExporterT3D()
        task.filename = str(Path(temporary) / "object.t3d")
        task.automated, task.prompt = True, False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Failed to export " + obj.get_path_name())
        data = Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig")

    for path in sorted(unreal.EditorAssetLibrary.list_assets(settings_root, recursive=True, include_folder=False)):
        settings = unreal.load_asset(path)
        if not isinstance(settings, unreal.AlsMantlingSettings):
            raise RuntimeError("Unexpected mantle settings asset " + path)
        get = settings.get_editor_property
        montage = get("montage")
        if not isinstance(montage, unreal.AnimMontage):
            raise RuntimeError("Missing mantle montage")
        heights, times, warp = get("start_time_reference_height"), get("start_time"), get("motion_warping_time_range")
        assets.append({"path": settings.get_path_name(), "montage": montage.get_path_name(),
            "autoCalculateStartTime": get("auto_calculate_start_time"),
            "referenceHeight": [heights.get_editor_property("x"), heights.get_editor_property("y")],
            "startTime": [times.get_editor_property("x"), times.get_editor_property("y")],
            "warpRange": [warp.get_editor_property("min"), warp.get_editor_property("max")],
            "locationBlend": get("motion_warping_location_blend_option").name,
            "rotationBlend": get("motion_warping_rotation_blend_option").name,
            "nativeText": native_text(settings)})
        montages[montage.get_path_name()] = montage
    if not assets:
        raise RuntimeError("No mantle settings found")

    character_class = unreal.load_class(None, character_path + "_C")
    subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    actor = subsystem.spawn_actor_from_class(character_class, unreal.Vector(0,0,-100000), transient=True)
    if actor is None:
        raise RuntimeError("Could not create transient mantle selection actor")
    try:
        general = actor.get_editor_property("settings")
        general_settings = {"path": general.get_path_name(), "nativeText": native_text(general)}
        for overlay in ("Default", "Masculine", "Feminine", "Injured", "HandsTied", "Rifle",
                        "PistolOneHanded", "PistolTwoHanded", "Bow", "Torch", "Binoculars", "Box", "Barrel"):
            tag_name = "Als.OverlayMode." + overlay
            tag = unreal.GameplayTag()
            if not tag.import_text('(TagName="' + tag_name + '")'):
                raise RuntimeError("Could not read registered overlay tag")
            actor.set_editor_property("overlay_mode", tag)
            actual_tag = str(actor.get_editor_property("overlay_mode").get_editor_property("tag_name"))
            if actual_tag != tag_name:
                raise RuntimeError(f"Overlay assignment failed: {actual_tag} != {tag_name}")
            for kind in (unreal.AlsMantlingType.HIGH, unreal.AlsMantlingType.LOW, unreal.AlsMantlingType.IN_AIR):
                selected = actor.call_method("SelectMantlingSettings", args=(kind,))
                if selected is None or selected.get_path_name() not in {row["path"] for row in assets}:
                    raise RuntimeError("Selection returned an unexported settings asset")
                cases.append({"overlay": tag_name, "type": kind.name, "settings": selected.get_path_name()})
    finally:
        subsystem.destroy_actor(actor)
    for graph in unreal.ObjectIterator(unreal.EdGraph):
        if graph.get_path_name().startswith(character_path + ":") and graph.get_name() == "SelectMantlingSettings":
            graphs.append({"path": graph.get_path_name(), "nativeText": native_text(graph)})
    if not graphs:
        raise RuntimeError("Missing native mantle selection graph")

    tracks = []
    for path, montage in sorted(montages.items()):
        length = montage.get_play_length()
        if not math.isfinite(length) or length <= 0:
            raise RuntimeError("Invalid mantle duration")
        samples = []
        times = [struct.unpack("f",struct.pack("f",i / 60))[0] for i in range(math.ceil(length * 60))] + [length]
        for time in sorted(set(times)):
            transform = unreal.AlsMontageUtility.extract_root_transform_from_montage(montage, time)
            p, q, s = transform.translation, transform.rotation, transform.scale3d
            if not all(math.isfinite(v) for v in (p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z)):
                raise RuntimeError("Nonfinite native root sample")
            samples.append({"time": time, "position": [p.x,p.y,p.z], "rotation": [q.x,q.y,q.z,q.w], "scale": [s.x,s.y,s.z]})
        tracks.append({"path": path, "length": length, "rateScale": montage.get_editor_property("rate_scale"),
            "nativeText": native_text(montage), "rootSamples": samples})

output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps({"schemaVersion": 1, "character": character_path, "engine": unreal.SystemLibrary.get_engine_version(),
    "selectionScope": "13 native overlay tags x High/Low/InAir; direct property assignment, selector only (not gameplay admission)",
    "generalSettings": general_settings, "settings": assets, "selection": cases,
    "graphs": sorted(graphs,key=lambda row: row["path"]), "montages": tracks}, indent=2) + "\n", encoding="utf-8", newline="\n")
unreal.log(f"ALS_MANTLE_INPUTS_OK settings={len(assets)} montages={len(tracks)} cases={len(cases)} assets_saved=0")

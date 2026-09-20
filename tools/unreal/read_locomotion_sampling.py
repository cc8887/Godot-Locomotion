"""Export native locomotion state graphs and BlendSpace settings without saving assets."""

import json
import os
from pathlib import Path

import unreal


output = Path(os.environ["ALS_SAMPLING_AUDIT_OUTPUT"])
if not output.is_absolute():
    raise ValueError("ALS_SAMPLING_AUDIT_OUTPUT must be absolute")
output.mkdir(parents=True, exist_ok=True)


def export_native(asset, filename):
    task = unreal.AssetExportTask()
    task.object = asset
    task.exporter = unreal.ObjectExporterT3D()
    task.filename = str(output / filename)
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    if not unreal.Exporter.run_asset_export_task(task):
        raise RuntimeError("Native export failed: " + asset.get_path_name())
    return {"source": asset.get_path_name(), "file": filename}


records = []
base = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/"
for direction in ("F", "B", "FL", "BL", "FR", "BR"):
    name = "ALS_N_WalkRun_" + direction
    asset = unreal.load_asset(base + name)
    if not isinstance(asset, unreal.BlendSpace):
        raise RuntimeError("Missing BlendSpace: " + name)
    records.append(export_native(asset, name + ".t3d"))

blueprint = unreal.load_asset("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP")
names = {"(N) Locomotion States", "ShouldMoveCheck", "CalculateWalkRunBlend", "CalculateStandingPlayRate"}
found = set()
for graph in unreal.BlueprintEditorLibrary.list_graphs(blueprint):
    name = graph.get_name()
    if name in names:
        if name in found:
            raise RuntimeError("Ambiguous graph: " + name)
        found.add(name)
        records.append(export_native(graph, "".join(c if c.isalnum() else "_" for c in name) + ".t3d"))
if found != names:
    raise RuntimeError("Missing graphs: " + repr(names - found))

(output / "index.json").write_text(json.dumps(records, indent=2) + "\n", encoding="utf-8")
unreal.log("ALS_SAMPLING_AUDIT_OK blendspaces=6 graphs=4 assets_saved=0")

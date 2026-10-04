"""Read original Blueprint and generated CDO control settings; do not save assets."""
import hashlib
import json
import os
import re
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
repo = Path(__file__).resolve().parents[2]
name = "rig_control_settings_v1.json"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
graph = json.loads((root / "footplant_rig_graph_v1.json").read_bytes())
previous = {str(p.relative_to(root)).replace("\\", "/"): sha(p) for p in root.rglob("*.json") if p.name != name}
if (root / name).exists():
    previous = json.loads((root / name).read_bytes())["previousFixtureSha256"]
packages = graph["assetSha256"]
content = Path(unreal.Paths.project_content_dir())


def protect():
    for p, h in previous.items():
        assert sha(root / p) == h, p
    for p, h in packages.items():
        assert sha(content / (p.split(".")[0].removeprefix("/Game/") + ".uasset")) == h, p


def settings(hierarchy):
    assert hierarchy
    result = {}
    for key in hierarchy.get_all_keys():
        if key.type != unreal.RigElementType.CONTROL:
            continue
        s = hierarchy.get_control_settings(key)
        # Transform is Hidden in ERigControlType; Python enum conversion rejects
        # it. Native property ExportText preserves the real enum name.
        text = s.export_text()
        control_type = re.search(r"(?:^|[, (])ControlType=([A-Za-z0-9_]+)", text)
        assert control_type, text
        result[str(key.name)] = dict(controlType=control_type.group(1),
                                    primaryAxis=str(s.get_editor_property("primary_axis")),
                                    limits=[dict(minimum=v.get_editor_property("minimum"),
                                                 maximum=v.get_editor_property("maximum"))
                                            for v in s.get_editor_property("limit_enabled")])
    assert len(result) == 7
    return result


protect()
blueprint = unreal.load_asset(graph["rig"])
asset = settings(blueprint.get_editor_property("hierarchy"))
cdo = unreal.get_default_object(unreal.load_class(None, graph["rig"] + "_C"))
generated = settings(cdo.get_hierarchy())
assert asset == generated
data = dict(schemaVersion=1, controls=generated, assetAndGeneratedCdoMatch=True,
            previousFixtureSha256=previous, assetSha256=packages)
protect()
p = root / name
if p.exists():
    assert json.loads(p.read_bytes()) == data
else:
    p.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
protect()
unreal.log("LYRA_RIG_CONTROL_SETTINGS_NATIVE_OK controls=7 cdoMatch=true assets_saved=0")

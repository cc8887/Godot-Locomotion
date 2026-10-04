"""Accept original FootPlant single-parent hierarchy; not pose transfer/full Rig."""
import hashlib
import json
import re
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / "assets/generated/lyra_als"
logs = repo / "artifacts/lyra-analysis"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())


def read(name):
    raw = (logs / name).read_bytes()
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        return raw.decode("utf-16")
    try:
        return raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return raw.decode("gb18030")


native = load("rig_hierarchy_v1_native.json")
settings = load("rig_control_settings_v1.json")
requests = load("rig_hierarchy_v1_requests.json")
program = load("footplant_rig_ground_v2_program.json")
assert native["requestSha256"] == sha(root / "rig_hierarchy_v1_requests.json")
assert native["counts"] == dict(traces=10, batches=37, reads=248, writes=796, resets=4)
assert settings["assetAndGeneratedCdoMatch"] and len(settings["controls"]) == 7
for c in settings["controls"].values():
    assert c["controlType"] == "Transform" and len(c["limits"]) == 9
    assert not any(v["minimum"] or v["maximum"] for v in c["limits"])
for fixture in (native, settings):
    for field in ("dependencies", "previousFixtureSha256"):
        for p, h in fixture.get(field, {}).items():
            assert sha(root / p) == h, p
    for p, h in fixture["assetSha256"].items():
        assert sha(project_path('Content') / (p.split(".")[0].removeprefix("/Game/") + ".uasset")) == h, p
for p, h in native["probeSourceSha256"].items():
    assert sha(repo / "tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter" / p) == h
    for tree in ("source", "package"):
        assert sha(repo / "artifacts/unreal/gasp58-lyra-masks" / tree / "AlsV4AssetExporter/Source/AlsV4AssetExporter" / p) == h
for t, q in zip(native["traces"], requests["traces"], strict=True):
    assert t["name"] == q["name"] and t["initial"] == program["initial"]["hierarchy"]
    assert len(t["layout"]) == 98
    for name, row in t["layout"].items():
        assert len(row["parents"]) <= 1
        if row["type"] == "Control":
            assert row["limitsEnabled"] is False and row["animationChannel"] is False
            assert len(row["weights"]) == len(row["parents"])
            assert all(w == dict(current=[1, 1, 1], initial=[1, 1, 1]) for w in row["weights"])
    for r, b in zip(t["batches"], q["batches"], strict=True):
        assert len(r["hierarchy"]) == 98
        assert len(r["reads"]) == sum(o["action"] == "read" for o in b["operations"])
checks = {}
for name, marker, mode in (
    ("lyra-rig-hierarchy-ue.log", "LYRA_RIG_HIERARCHY_NATIVE_OK traces=10 batches=37 reads=248 writes=796 resets=4 assets_saved=0", "rig-hierarchy"),
    ("lyra-rig-hierarchy-repeat-ue.log", "LYRA_RIG_HIERARCHY_NATIVE_OK traces=10 batches=37 reads=248 writes=796 resets=4 assets_saved=0", "rig-hierarchy"),
    ("lyra-rig-control-settings-ue.log", "LYRA_RIG_CONTROL_SETTINGS_NATIVE_OK controls=7 cdoMatch=true assets_saved=0", "rig-control-settings"),
    ("lyra-rig-control-settings-repeat-ue.log", "LYRA_RIG_CONTROL_SETTINGS_NATIVE_OK controls=7 cdoMatch=true assets_saved=0", "rig-control-settings")):
    text = read(name)
    assert text.count(marker) == 1 and ": Error:" not in text
    if name == "lyra-rig-control-settings-ue.log":
        assert "LYRA_EXPORT_PROCESS_EXIT mode=rig-control-settings code=0" in text
    else:
        assert f"LYRA_EXPORT_PROCESS_EXIT_OK mode={mode} code=0" in text
    checks[name] = dict(sha256=sha(logs / name), warnings=len(re.findall(r": Warning:", text)), errors=0)
hierarchy_marker = ("LYRA_RIG_HIERARCHY_GODOT_OK batches=37 writes=796 reads=248 resets=4 retries=37 "
                    "rejects=20 transforms=35776 maxP=3.552713678800501E-15 maxQ=0 maxS=0 fullRig=false")
construction_marker = ("LYRA_FOOTPLANT_RIG_CONSTRUCTION_GODOT_OK constructions=12 transforms=5040 controls=84 "
                       "frames=2520 leftHits=2313 rightHits=2303 exact=true fullRig=false")
for name, marker in (
    ("rig-hierarchy-godot-debug.log", hierarchy_marker),
    ("rig-hierarchy-godot-optimize.log", hierarchy_marker),
    ("rig-hierarchy-construction-regression-debug.log", construction_marker)):
    text = read(name)
    assert text.count(marker) == 1 and "LYRA_GODOT_PROCESS_EXIT code=0" in text
    assert not re.search(r"^\s*(ERROR|WARNING):", text, re.M)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
optimized = read("rig-hierarchy-godot-optimize.log")
assert optimized.count(construction_marker) == 1 and "LYRA_CONSTRUCTION_REGRESSION_PROCESS_EXIT code=0" in optimized
assert optimized.count("LYRA_OPTIMIZED_ASSEMBLY") == 3 and "LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true" in optimized
for name in ("rig-hierarchy-debug-build.log", "rig-hierarchy-optimize-build.log"):
    text = read(name)
    assert re.search(r"0\s*(个警告|Warning)", text) and re.search(r"0\s*(个错误|Error)", text)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
source_files = ["src/Als.Godot/Animation/Lyra/LyraFootPlantRigHierarchy.cs",
                "src/Als.Godot/Animation/Lyra/LyraFootPlantRigConstruction.cs",
                "src/Als.Godot/Animation/Lyra/LyraRigHierarchySmoke.cs",
                "tools/unreal/export_lyra_rig_control_settings.py",
                "tools/unreal/export_lyra_rig_hierarchy.py"]
report = dict(stage="OriginalFootPlantHierarchyWrites", accepted=True, fullRigPoseAccepted=False,
              poseTransferAccepted=False, production=False, counts=native["counts"],
              nativeSha256=sha(root / "rig_hierarchy_v1_native.json"),
              settingsSha256=sha(root / "rig_control_settings_v1.json"),
              maxPositionCm=3.552713678800501e-15, maxQuaternion=0, maxScale=0,
              transformsComparedPerConfiguration=35776,
              protectedPackages=len(native["assetSha256"]),
              protectedFixtures=len(native["previousFixtureSha256"]),
              settingsProtectedFixtures=len(settings["previousFixtureSha256"]),
              sourceSha256={p: sha(repo / p) for p in source_files}, checks=checks)
(logs / "lyra-rig-hierarchy-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("LYRA_RIG_HIERARCHY_VERIFIED fullRig=false poseTransfer=false production=false")

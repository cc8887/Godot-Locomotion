"""Accept the ground fixture and bounded Construction; never full Rig/Demo."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / "assets/generated/lyra_als"
logs = repo / "artifacts/lyra-analysis"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda name: json.loads((root / name).read_bytes())


def read(name):
    raw = (logs / name).read_bytes()
    if raw.startswith((b"\xff\xfe", b"\xfe\xff")):
        return raw.decode("utf-16")
    try:
        return raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return raw.decode("gb18030")


prefix = "footplant_rig_ground_v2_"
native = load(prefix + "native.json")
program = load(prefix + "program.json")
requests = load(prefix + "requests.json")
update = load(prefix + "update.json")
for kind in ("native", "program", "request", "policy"):
    file_kind = "requests" if kind == "request" else kind
    assert update[kind + "Sha256"] == sha(root / (prefix + file_kind + ".json"))
for field in ("dependencies", "previousFixtureSha256"):
    for p, h in native[field].items():
        assert sha(root / p) == h, p
for p, h in native["assetSha256"].items():
    assert sha(Path("../GASP58/Content") / (p.split(".")[0].removeprefix("/Game/") + ".uasset")) == h, p
for p, h in native["probeSourceSha256"].items():
    assert sha(repo / "tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter" / p) == h
    for tree in ("source", "package"):
        assert sha(repo / "artifacts/unreal/gasp58-lyra-masks" / tree / "AlsV4AssetExporter/Source/AlsV4AssetExporter" / p) == h
assert len(program["instructions"]) == 436 and len(program["functions"]) == 40
assert len(program["initial"]["hierarchy"]) == 98
counts = dict(frames=0, poses=0, leftHits=0, rightHits=0, pelvisOffset=0, slope=0, slopeCrouching=0, sanityHits=0)
for t, q in zip(native["traces"], requests["traces"], strict=True):
    assert (t["mode"], t["hz"]) == (q["mode"], q["hz"])
    assert t["traceChannelName"] == "Traversable" and t["groundResponse"] == 2
    assert t["program"]["initial"]["hierarchy"] == program["initial"]["hierarchy"]
    for row, request in zip(t["frames"], q["frames"], strict=True):
        counts["frames"] += 1
        assert row["groundSanityHit"] == request["geometry"]
        counts["sanityHits"] += row["groundSanityHit"]
        if "output" not in row:
            continue
        counts["poses"] += 1
        assert len(row["output"]["pose"]) == len(row["input"]["pose"]) == 81
        variables = row["after"]["variables"]
        counts["leftHits"] += variables["DidLeftFootTraceHit"]
        counts["rightHits"] += variables["DidRightFootTraceHit"]
        counts["pelvisOffset"] += variables["CurrentPelvisOffsetZ"] != 0
        counts["slope"] += variables["isCharacterSloping"]
        counts["slopeCrouching"] += variables["isSlopingAndCrouching"]
assert all(native["counts"][k] == v for k, v in counts.items())
assert counts == dict(frames=2520, poses=2154, leftHits=1947, rightHits=1937,
                     pelvisOffset=2112, slope=1992, slopeCrouching=636, sanityHits=2400)

# Certify the old fixture's limit without rewriting its pinned bytes.
old = load("footplant_rig_v1_native.json")
old_hits = sum(r["after"]["variables"]["DidLeftFootTraceHit"] or r["after"]["variables"]["DidRightFootTraceHit"]
               for t in old["traces"] for r in t["frames"] if "output" in r)
assert old_hits == 0
checks = {}
for name in ("lyra-footplant-rig-ground-ue.log", "lyra-footplant-rig-ground-repeat-ue.log"):
    text = read(name)
    assert text.count("LYRA_FOOTPLANT_RIG_GROUND_NATIVE_OK frames=2520 poses=2154 partial=164 leftHits=1947 rightHits=1937 pelvis=2112 slope=1992 assets_saved=0") == 1
    assert "LYRA_EXPORT_PROCESS_EXIT_OK mode=footplant-rig-ground code=0" in text and ": Error:" not in text
    checks[name] = dict(sha256=sha(logs / name), warnings=len(re.findall(r": Warning:", text)), errors=0)
assert "allOtherValuesExact=true originalBytesPreserved=true" in read("lyra-footplant-rig-ground-repeat-ue.log")
marker = ("LYRA_FOOTPLANT_RIG_CONSTRUCTION_GODOT_OK constructions=12 transforms=5040 controls=84 "
          "frames=2520 leftHits=2313 rightHits=2303 exact=true fullRig=false")
for name in ("footplant-rig-construction-godot-debug.log", "footplant-rig-construction-godot-optimize.log"):
    text = read(name)
    assert text.count(marker) == 1 and "LYRA_GODOT_PROCESS_EXIT code=0" in text
    assert not re.search(r"^\s*(ERROR|WARNING):", text, re.M)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
for name in ("footplant-rig-construction-debug-build.log", "footplant-rig-construction-optimize-build.log"):
    text = read(name)
    assert re.search(r"0\s*(个警告|Warning)", text) and re.search(r"0\s*(个错误|Error)", text)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
optimized = read("footplant-rig-construction-godot-optimize.log")
assert optimized.count("LYRA_OPTIMIZED_ASSEMBLY") == 3 and "LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true" in optimized
report = dict(stage="Main73ConstructionAndPositiveContactFixture", accepted=True,
              fullRigPoseAccepted=False, production=False, counts=counts,
              construction=dict(independentAttempts=12, transformsExact=5040, controls=84,
                                variableHistoryFrames=2520, changedOffsetWriteNativeCoverage=False),
              oldFixturePositiveContactFrames=old_hits,
              nativeSha256=sha(root / (prefix + "native.json")),
              preservedFixtures=len(native["previousFixtureSha256"]),
              protectedPackages=len(native["assetSha256"]), checks=checks)
(logs / "lyra-footplant-rig-construction-verification.json").write_text(
    json.dumps(report, indent=2), encoding="utf-8")
print("LYRA_FOOTPLANT_RIG_CONSTRUCTION_VERIFIED fullRig=false production=false")

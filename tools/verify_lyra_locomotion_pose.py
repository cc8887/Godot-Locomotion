"""Verify the scoped native composition evidence and preserve its hashes."""
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--content-root", type=Path, default=repo.parent / "GASP58/Content")
args = parser.parse_args()
artifacts = repo / "artifacts/lyra-analysis"
assets = repo / "assets/generated/lyra_als"
sha = lambda data: hashlib.sha256(data).hexdigest()

def require(condition, message):
    if not condition:
        raise ValueError(message)

def read_json(path):
    return json.loads(path.read_bytes())

def checked_log(name, marker):
    data = (artifacts / name).read_text(encoding="utf-8-sig")
    require(data.count(marker) == 1, "Missing/duplicate success marker: " + name)
    require(not re.search(r"(?im)^(?:ERROR|WARNING):", data), "Godot error/warning: " + name)
    return data

oracle_path = assets / "locomotion_blend_native.json"
oracle = read_json(oracle_path)
request_path = artifacts / "locomotion-blend-requests.json"
inputs = read_json(request_path)
require(oracle["schemaVersion"] == 1 and oracle["requestSha256"] == sha(request_path.read_bytes()), "Stale native inputs")
for field, path in [("runtimeGraphSha256", assets / "runtime_graph.json"),
                    ("calibrationSha256", assets / "logical_controls/calibration.json"),
                    ("catalogSha256", assets / "logical_controls/catalog.json")]:
    digest = sha(path.read_bytes())
    require(oracle[field] == inputs[field] == digest, "Stale oracle dependency: " + field)
require([t["hz"] for t in oracle["traces"]] == [30, 60, 120] and
        sum(len(t["frames"]) for t in oracle["traces"]) == 840, "Incomplete native trace")
for path, digest in oracle["assetSha256"].items():
    require(path.startswith("/Game/"), "Unexpected source asset")
    package = args.content_root / (path.split(".", 1)[0][6:] + ".uasset")
    require(sha(package.read_bytes()) == digest, "Changed UE package: " + path)
calibration = read_json(assets / "logical_controls/calibration.json")
for path, digest in calibration["dependencySha256"].items():
    require(sha((assets / path).read_bytes()) == digest, "Changed export dependency: " + path)
catalog = read_json(assets / "logical_controls/catalog.json")
for entry in catalog["entries"]:
    require(sha((assets / "logical_controls" / entry["file"]).read_bytes()) == entry["sha256"], "Changed raw clip: " + entry["slot"])

main = checked_log("locomotion-pose-smoke-twist.log", "LYRA_LOCOMOTION_POSE_OK")
require("LYRA_LOCOMOTION_POSE_DIAGNOSTICS failures=0" in main, "Native pose mismatch")
line = next(line for line in main.splitlines() if line.startswith("LYRA_LOCOMOTION_POSE_MEASURED "))
measurements = dict(re.findall(r"([A-Za-z]+)=([^ ]+)", line))
require(measurements["frames"] == "840" and measurements["sourceUpdates"] == "1561" and
        measurements["inertiaRequests"] == "18" and measurements["precleanupUpdates"] == "30" and
        measurements["depth"] == "3" and measurements["retry"] == "True", "Wrong comparison coverage")
checked_log("locomotion-pose-machine-regression.log", "LYRA_LOCOMOTION_MACHINE_OK")
checked_log("locomotion-pose-binding-regression.log", "LYRA_LINKED_BINDING_OK")
checked_log("locomotion-pose-demo-regression-60-final.log", "LYRA_RIFLE_SWITCH_OK hz=60 frames=870 ")
tests = {}
namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
for name, count in [("lyra-pose-core-regression.trx", 42), ("lyra-pose-standing-native-regression.trx", 6)]:
    counters = ET.parse(artifacts / name).find("t:ResultSummary/t:Counters", namespace).attrib
    require(int(counters["total"]) == count and int(counters["passed"]) == count and
            int(counters["failed"]) == 0 and int(counters["notExecuted"]) == 0, "Failed regression: " + name)
    tests[name] = counters
for name in ["locomotion-pose-build-twist.log", "locomotion-pose-optimize-final.log"]:
    data = (artifacts / name).read_text(encoding="utf-8-sig")
    require("0 个警告" in data and "0 个错误" in data, "Build errors/warnings: " + name)
ue = (artifacts / "locomotion-blend-ue-full.log").read_text(encoding="utf-8-sig")
require(ue.count("LYRA_LOCOMOTION_BLEND_NATIVE_OK hz=30,60,120 frames=840 bones=81 assets_saved=0") == 1 and
        ": Error:" not in ue, "Native exporter failure")
report = {"schemaVersion": 1, "scope": "Controlled raw81 main-machine composition and final inertia; original production source host remains open.",
          "oracleSha256": sha(oracle_path.read_bytes()), "requestSha256": oracle["requestSha256"],
          "measurements": measurements, "regressions": tests,
          "ue": {"assetsSaved": 0, "errorOccurrences": ue.count(": Error:"), "warningOccurrences": ue.count(": Warning:")},
          "unchangedPackages": len(oracle["assetSha256"]), "unchangedDependencyJson": len(calibration["dependencySha256"]),
          "unchangedRawClips": len(catalog["entries"]), "demoPhysicalFrames": 870,
          "limitations": ["No production main/source-host replacement", "No original linked source graph/Sync/Notify/attributes",
                          "No new GPU render, full manual matrix, ten-minute or performance acceptance"],
          "evidenceSha256": {path.name: sha(path.read_bytes()) for path in [request_path,
              *[artifacts / name for name in ["locomotion-blend-native-build-access.log", "locomotion-blend-export-first.log",
                  "locomotion-blend-ue-full.log", "locomotion-pose-smoke-twist.log", "locomotion-pose-build-twist.log",
                  "locomotion-pose-optimize-final.log", "locomotion-pose-machine-regression.log", "locomotion-pose-binding-regression.log",
                  "locomotion-pose-demo-regression-60-final.log", *tests.keys()]] ]}}
output = artifacts / "locomotion-pose-final-verification.json"
output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"LYRA_LOCOMOTION_POSE_VERIFIED frames=840 packages={report['unchangedPackages']} dependencies={report['unchangedDependencyJson']} clips={report['unchangedRawClips']} core=42 standingNative=6")

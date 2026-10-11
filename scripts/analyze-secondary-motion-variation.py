"""Check temporal amplification against real ordinary-Demo skeleton and sole reports."""
import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def angle(a, b):
    dot = abs(sum(x * y for x, y in zip(a, b)))
    norm = math.sqrt(sum(x * x for x in a) * sum(x * x for x in b))
    return 2 * math.degrees(math.acos(min(1, dot / norm)))


def analyze(directory):
    cases = ["native-60-single", "enhanced-60-single", "enhanced-60-parallel",
             "enhanced-30-single", "enhanced-120-single", "enhanced-60-render-framed",
             "native-60-render-framed"]
    reports = {name: json.loads((directory / (name + ".json")).read_text()) for name in cases}
    for name, report in reports.items():
        rows = report["frames"]
        assert [row["tick"] for row in rows] == list(range(3, report["hz"] * 8 + 1)), name
        assert all(row["committed"] == row["tick"] for row in rows), name
        assert report["sources"]["allocated"] == 0, name
        assert report["sources"]["changedPlayers"] == (3 if report["enhanced"] else 0), name
        assert all(value < .5 for value in report["sources"]["loopSeamDegrees"].values()), name
        for side in ("Item1", "Item2"):
            # Godot instance IDs are process-local. Check their stability in each run;
            # geometric comparisons below exclude only this address-like value.
            assert len({row["soles"][side]["SurfaceId"] for row in rows}) == 1, name
            assert all(row["soles"][side]["MinimumSupportHeight"] >= -2 for row in rows), name
    original = reports["native-60-single"]["frames"]
    enhanced = reports["enhanced-60-single"]["frames"]
    assert enhanced == reports["enhanced-60-parallel"]["frames"], "Parallel frames differ"
    assert all(a["bones"][bone] == b["bones"][bone] for a, b in zip(original, enhanced)
               for bone in ("root", "pelvis", "foot_l", "foot_r")), "Lower-body pose changed"
    assert all(a["soles"] == b["soles"] for a, b in zip(original, enhanced)), "Skinned soles changed"

    def sole_values(row):
        return {side: {key: value for key, value in row["soles"][side].items() if key != "SurfaceId"}
                for side in ("Item1", "Item2")}

    for name, reference in (("enhanced-60-render-framed", enhanced), ("native-60-render-framed", original)):
        report = reports[name]
        assert all(a["bones"] == b["bones"] and sole_values(a) == sole_values(b)
                   for a, b in zip(report["frames"], reference)), name + " rendered pose differs"
        assert [capture["tick"] for capture in report["captures"]] == [120, 180, 240, 300], name
        for capture in report["captures"]:
            path = Path(capture["path"])
            assert (path if path.is_absolute() else ROOT / path).is_file(), capture

    gains = dict(spine_01=4, spine_02=4, spine_03=4, clavicle_l=2, clavicle_r=2, neck_01=2, head=2)
    amplitudes = {}
    for bone, gain in gains.items():
        values = []
        for rows in (original, enhanced):
            first = rows[120]["bones"][bone]["rotation"]
            values.append(max(angle(row["bones"][bone]["rotation"], first) for row in rows[120:]))
        ratio = values[1] / values[0]
        assert abs(ratio / gain - 1) < .05, (bone, values, ratio)
        amplitudes[bone] = dict(originalDegrees=values[0], enhancedDegrees=values[1], ratio=ratio)
    integrity = json.loads((directory / "asset-integrity.json").read_text())
    assert not integrity["errors"], integrity
    return dict(passed=True, cases=len(cases), observedFrames=sum(len(r["frames"]) for r in reports.values()),
                captures=sum(len(r["captures"]) for r in reports.values()),
                singleParallelExact=True, lowerBodyExact=True, skinnedSolesExact=True,
                renderedBonesAndSoleGeometryExact=True, amplitudes=amplitudes, assetIntegrity=integrity)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    args = parser.parse_args()
    result = analyze(args.directory)
    (args.directory / "summary.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))

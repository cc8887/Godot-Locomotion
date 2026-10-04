"""Summarize native control motion and quantify fresh-FK reconstruction loss."""
import argparse
import hashlib
import json
import math
from pathlib import Path


def distance(a, b):
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def normalize(q):
    length = math.sqrt(sum(v * v for v in q))
    return [v / length for v in q]


def angle(a, b):
    return 2 * math.acos(min(1, abs(sum(x * y for x, y in zip(normalize(a), normalize(b))))))


def multiply(a, b):
    x, y, z, w = a
    X, Y, Z, W = b
    return [w * X + x * W + y * Z - z * Y,
            w * Y - x * Z + y * W + z * X,
            w * Z + x * Y - y * X + z * W,
            w * W - x * X - y * Y - z * Z]


def rotate(q, v):
    return multiply(multiply(q, [*v, 0]), [-q[0], -q[1], -q[2], q[3]])[:3]


def components(atoms, parents):
    result = []
    for atom, parent in zip(atoms, parents):
        if any(value < 0 for value in atom["scale"]):
            raise ValueError("The audit's direct TRS path does not support negative scales")
        if parent < 0:
            result.append(atom)
            continue
        base = result[parent]
        p = rotate(base["rotation"], [a * b for a, b in zip(atom["position"], base["scale"])])
        result.append({"position": [a + b for a, b in zip(p, base["position"])],
                       "rotation": normalize(multiply(base["rotation"], atom["rotation"])),
                       "scale": [a * b for a, b in zip(base["scale"], atom["scale"])]})
    return result


def analyze(root):
    source_bytes = (root / "weapon_space_sources.json").read_bytes()
    native_bytes = (root / "weapon_space_native.json").read_bytes()
    sources = json.loads(source_bytes)
    native = json.loads(native_bytes)
    if native["sourceDataSha256"] != hashlib.sha256(source_bytes).hexdigest():
        raise ValueError("Native oracle has stale source channels")
    for name, sha in sources["dependencySha256"].items():
        if hashlib.sha256((root / name).read_bytes()).hexdigest() != sha:
            raise ValueError("Stale dependency: " + name)
    layout = sources["layout"]
    names, parents = layout["logicalBoneNames"], layout["logicalParents"]
    weapon, vb, left = (names.index(name) for name in ("weapon_r", "VB IK_Hand_L_weaponSpace", "hand_l"))
    assert parents[weapon] == names.index("hand_r") and parents[vb] == weapon
    assert next(v for v in layout["virtualBones"] if v["bone"] == vb) == {"bone": vb, "source": weapon, "target": left}
    profiles = {}
    for profile in ("unarmed", "pistol", "rifle"):
        ordinary = [c for c in sources["clips"] if not c["additive"] and
                    (c["category"].startswith("unarmed_") if profile == "unarmed" else c["category"] == profile + "_catalog.json")]
        additive = [c for c in sources["clips"] if c["additive"] and c["category"] == profile]
        stats = {"ordinaryClips": len(ordinary), "additiveClips": len(additive)}
        for remainder, label in ((0, "Keys"), (1, "Midpoints")):
            errors = [(distance(sample["component"][1]["position"], sample["component"][3]["position"]),
                       clip["slot"], sample["seconds"])
                      for clip in ordinary for index, sample in enumerate(clip["samples"]) if index % 2 == remainder]
            value, slot, seconds = max(errors)
            stats["ordinaryFreshFkMaxPositionCmAt" + label] = {"value": value, "slot": slot, "seconds": seconds}
        for index, label in ((2, "weapon"), (3, "virtual")):
            for field, measure, suffix in (("position", distance, "PositionCm"), ("rotation", angle, "RotationRadians")):
                candidates = [(measure(clip["samples"][0]["local"][index][field], sample["local"][index][field]),
                               clip["slot"], sample["seconds"])
                              for clip in ordinary for sample in clip["samples"]]
                value, slot, seconds = max(candidates)
                stats[label + "MaxWithinClip" + suffix] = {"value": value, "slot": slot, "seconds": seconds}
            stats[label + "MaxAdditivePositionCm"] = max(
                distance(sample["local"][index]["position"], [0, 0, 0]) for clip in additive for sample in clip["samples"])
        native_cases = [row for row in native["rows"] if row["profile"] == profile and row["alpha"] == 1]
        errors = []
        for row in native_cases:
            pose = components(row["before"], parents)
            errors.append((distance(pose[vb]["position"], pose[left]["position"]),
                           angle(pose[vb]["rotation"], pose[left]["rotation"]), row["slot"], row["input"]))
        p = max(errors, key=lambda e: e[0]); q = max(errors, key=lambda e: e[1])
        stats["freshFkAfterAimMaxPositionCm"] = {"value": p[0], "slot": p[2], "input": p[3]}
        stats["freshFkAfterAimMaxRotationRadians"] = {"value": q[1], "slot": q[2], "input": q[3]}
        profiles[profile] = stats
    return {"schemaVersion": 1,
            "sourceSha256": hashlib.sha256(source_bytes).hexdigest(),
            "nativeSha256": hashlib.sha256(native_bytes).hexdigest(),
            "ordinaryClips": 189, "additiveClips": 45,
            "evaluatedSamples": sum(len(c["samples"]) for c in sources["clips"]),
            "nativeCases": len(native["rows"]), "profiles": profiles,
            "basis": "Native Manny local controls before graph blends; actual AimOffset applied to Idle/Cycle",
            "limits": "Finite key/midpoint and Aim input sample set; no ALS control retarget or production left-hand IK"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = analyze(args.root)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report["profiles"], indent=2))

"""Verify native pre-warp Cycle output coverage and immutable dependencies."""
import argparse
import hashlib
import json
import math
from pathlib import Path
from locomotion_paths import project_path

from verify_lyra_cycle_layer import verify as verify_layer

def sha(data):
    return hashlib.sha256(data).hexdigest()

def verify(root, content):
    source = verify_layer(root, content)
    policy_bytes = (root / "cycle_layer_pose_policy.json").read_bytes()
    native_bytes = (root / "cycle_layer_pose_native_v2.json").read_bytes()
    policy, native = map(json.loads, (policy_bytes, native_bytes))
    if policy["schemaVersion"] != 1 or native["schemaVersion"] != 1 or policy["stage"] != "AuthoredPreWarp" or policy["generatedRootMotion"]:
        raise ValueError("Wrong Cycle pose scope")
    if native["policySha256"] != sha(policy_bytes) or native["dependencies"] != policy["dependencies"]:
        raise ValueError("Stale Cycle pose policy")
    for name, digest in policy["dependencies"].items():
        if sha((root / name).read_bytes()) != digest:
            raise ValueError("Changed Cycle pose dependency: " + name)
    masks = json.loads((root / "unarmed_layer_masks.json").read_bytes())["profiles"]["UpperBodyMask"]
    layout = json.loads((root / "logical_controls/calibration.json").read_bytes())["layout"]["logicalBoneNames"]
    source_weights = {b["bone"].casefold(): b["scale"] for b in masks["sourceBones"]}
    expected_mask = [source_weights.get(name.casefold(), 0) for name in layout]
    for value in policy["policies"].values():
        if value["mask"] != expected_mask or len(value["attributes"]) != 4 or value["curveBindings"]:
            raise ValueError("Changed native mask/attribute/linked-curve policy")
        if any(a["type"] != "/Script/Engine.IntegerAnimationAttribute" or a["namespace"] != "bone" or
               a["bone"].casefold() != "pelvis" or a["blend"] != "Blend" for a in value["attributes"]):
            raise ValueError("Changed authored attribute policy")
    clocks = json.loads((root / "cycle_layer_native_bits.json").read_bytes())["traces"]
    if len(native["traces"]) != 9 or len(native["probes"]) != 72:
        raise ValueError("Incomplete native Cycle pose traces/probes")
    frames = bones = curves = attributes = 0
    for trace, clock in zip(native["traces"], clocks, strict=True):
        if (trace["profile"], trace["hz"]) != (clock["profile"], clock["hz"]):
            raise ValueError("Cycle pose trace order changed")
        active = [index for index, frame in enumerate(clock["frames"]) if frame["active"]]
        if [row["frame"] for row in trace["rows"]] != active:
            raise ValueError("Missing/reordered active Cycle pose frames")
        for row in trace["rows"]:
            output = row["output"]
            if len(output["pose"]) != 81 or len(output["attributes"]) != 4:
                raise ValueError("Incomplete native pose/attributes")
            for atom in output["pose"]:
                if (len(atom["position"]), len(atom["rotation"]), len(atom["scale"])) != (3, 4, 3) or not all(
                        math.isfinite(v) for values in atom.values() for v in values):
                    raise ValueError("Invalid native transform")
                if abs(sum(v*v for v in atom["rotation"]) - 1) > 1e-5:
                    raise ValueError("Unnormalized native rotation")
            if any(not math.isfinite(c["value"]) or not isinstance(c["flags"], int) for c in output["curves"].values()):
                raise ValueError("Invalid native curve")
            frames += 1
            bones += len(output["pose"])
            curves += len(output["curves"])
            attributes += len(output["attributes"])
    if (frames, bones, curves, attributes) != (3528, 285768, 105, 14112):
        raise ValueError("Changed Cycle pose output coverage")
    probes = {(row["override"], row["presence"], row["weight"]) for row in native["probes"]}
    if len(probes) != 72:
        raise ValueError("Duplicate native data probe")
    return {"frames": frames, "bones": bones, "curves": curves, "attributes": attributes, "probes": len(probes),
            "packages": source["packages"], "stage": "AuthoredPreWarp", "generatedRootMotion": False,
            "production": False, "wholeMain": False, "files": {
                "cycle_layer_pose_policy.json": {"sha256": sha(policy_bytes), "bytes": len(policy_bytes)},
                "cycle_layer_pose_native_v2.json": {"sha256": sha(native_bytes), "bytes": len(native_bytes)}}}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=project_path('Content'))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/cycle-pose-verification.json"))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_CYCLE_POSE_VERIFIED frames=3528 bones=285768 curves=105 attributes=14112 probes=72 packages=" + str(result["packages"]))

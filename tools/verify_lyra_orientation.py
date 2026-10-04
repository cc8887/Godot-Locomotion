"""Validate native Orientation outputs and prove the fixture actually warps."""
import argparse
import hashlib
import json
import math
from pathlib import Path
from verify_lyra_root_motion import verify as verify_root

sha = lambda data: hashlib.sha256(data).hexdigest()


def verify(root, content):
    previous = verify_root(root, content)
    policy_bytes = (root / "orientation_policy.json").read_bytes()
    request_bytes = (root / "orientation_requests.json").read_bytes()
    policy, requests = map(json.loads, (policy_bytes, request_bytes))
    native = json.loads((root / "orientation_native_v2.json").read_bytes())
    before = json.loads((root / "root_motion_native.json").read_bytes())
    clocks = json.loads((root / "cycle_layer_native_bits.json").read_bytes())
    if native["policySha256"] != sha(policy_bytes) or native["requestsSha256"] != sha(request_bytes):
        raise ValueError("Stale Orientation resource")
    for contract in (native, policy, requests):
        if contract["schemaVersion"] != 1 or contract["dependencies"] != policy["dependencies"]:
            raise ValueError("Inconsistent Orientation contract")
        for name, digest in contract["dependencies"].items():
            if sha((root / name).read_bytes()) != digest:
                raise ValueError("Changed Orientation dependency: " + name)
    expected_policy = {
        "originalSpines": ["spine_01", "spine_02", "spine_03", "spine_04", "spine_05", "ik_hand_root"],
        "adaptedSpines": ["spine_01", "spine_02", "spine_03", "ik_hand_root"],
        "hasPredictionAsset": False, "predictionTime": 0}
    if policy["policies"] != dict.fromkeys(("unarmed", "pistol", "rifle"), expected_policy):
        raise ValueError("Changed original node/ALS adaptation")
    counts = dict.fromkeys(("frames", "hidden", "poseChanged", "bonesChanged", "rootChanged", "rootPresent",
                           "partialAlpha", "zeroAlpha", "worldDirection", "relativeRotation", "reinitialize"), 0)
    if any(len(item["traces"]) != 9 for item in (native, requests, before, clocks)):
        raise ValueError("Incomplete Orientation traces")
    for trace, controls, old, clock in zip(native["traces"], requests["traces"], before["traces"], clocks["traces"], strict=True):
        identity = (trace["profile"], trace["hz"])
        if any((item["profile"], item["hz"]) != identity for item in (controls, old, clock)):
            raise ValueError("Reordered Orientation trace")
        active = [i for i, frame in enumerate(clock["frames"]) if frame["active"]]
        if [row["frame"] for row in trace["rows"]] != active or [row["frame"] for row in old["rows"]] != active:
            raise ValueError("Missing/reordered active output")
        if len(controls["frames"]) != len(clock["frames"]):
            raise ValueError("Missing physical frame controls")
        previous_index = -1
        for index, (control, frame) in enumerate(zip(controls["frames"], clock["frames"], strict=True)):
            if not frame["active"]:
                if control is not None:
                    raise ValueError("Hidden frame has node inputs")
                counts["hidden"] += 1
                continue
            if control is None or control["counter"] != index or control["ticks"] != index - previous_index or control["weight"] != frame["cycleWeight"]:
                raise ValueError("Changed counter/weight history")
            previous_index = index
            counts["partialAlpha"] += 0 < control["alpha"] < 1
            counts["zeroAlpha"] += control["alpha"] == 0
            counts["worldDirection"] += any(control["direction"])
            counts["relativeRotation"] += control["relativeRotation"] != [0, 0, 0, 1]
            counts["reinitialize"] += control["reinitialize"]
        for row, old_row in zip(trace["rows"], old["rows"], strict=True):
            output, base = row["output"], old_row["output"]
            if any(output[key] != base[key] for key in ("curves", "attributes")) or len(output["pose"]) != 81:
                raise ValueError("Warp changed curve/attribute inventory")
            for transform in output["pose"] + ([output["rootMotion"]] if "rootMotion" in output else []):
                if not all(math.isfinite(value) for key in ("position", "rotation", "scale") for value in transform[key]):
                    raise ValueError("Nonfinite Orientation output")
            if ("rootMotion" in output) != ("rootMotion" in base):
                raise ValueError("Warp changed generated attribute presence")
            changed = sum(x != y for x, y in zip(output["pose"], base["pose"], strict=True))
            counts["poseChanged"] += bool(changed)
            counts["bonesChanged"] += changed
            counts["rootChanged"] += output.get("rootMotion") != base.get("rootMotion")
            counts["rootPresent"] += "rootMotion" in output
            control = controls["frames"][row["frame"]]
            if control["alpha"] == 0 and output.get("rootMotion") != base.get("rootMotion"):
                raise ValueError("Filtered node altered RootMotion")
            if "rootMotion" in output:
                for key in ("name", "type", "bone", "namespace", "rotation", "scale"):
                    if output["rootMotion"][key] != base["rootMotion"][key]:
                        raise ValueError("Orientation changed nontranslation root fields")
            counts["frames"] += 1
    expected = dict(zip(counts, (3528, 252, 2661, 23632, 2395, 3255, 1401, 651, 1749, 2571, 18), strict=True))
    if counts != expected:
        raise ValueError("Incomplete material Warp coverage: " + repr(counts))
    if native["assetSha256"] != clocks["assetSha256"] or len(native["assetSha256"]) != previous["packages"]:
        raise ValueError("Changed native package provenance")
    return {**counts, "packages": previous["packages"], "stage": "AfterOrientation", "controlledInputs": True,
            "production": False, "wholeMain": False,
            "files": {name: {"sha256": sha((root / name).read_bytes()), "bytes": (root / name).stat().st_size}
                      for name in ("orientation_policy.json", "orientation_requests.json", "orientation_native_v2.json")}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=Path("../GASP58/Content"))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/orientation-verification.json"))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_ORIENTATION_VERIFIED " + " ".join(f"{key}={result[key]}" for key in ("frames", "poseChanged", "bonesChanged", "rootChanged", "rootPresent", "packages")))

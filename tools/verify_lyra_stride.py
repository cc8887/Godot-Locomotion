"""Verify original Stride output, unchanged data and material deformation."""
import argparse
import hashlib
import json
import math
from pathlib import Path
from verify_lyra_orientation import verify as verify_orientation

sha = lambda data: hashlib.sha256(data).hexdigest()


def verify(root, content):
    previous = verify_orientation(root, content)
    policy_bytes = (root / "stride_policy.json").read_bytes()
    request_bytes = (root / "stride_requests.json").read_bytes()
    policy, requests = map(json.loads, (policy_bytes, request_bytes))
    native = json.loads((root / "stride_native.json").read_bytes())
    before = json.loads((root / "orientation_native_v2.json").read_bytes())
    clock = json.loads((root / "cycle_layer_native_bits.json").read_bytes())
    if native["policySha256"] != sha(policy_bytes) or native["requestsSha256"] != sha(request_bytes):
        raise ValueError("Stale Stride resources")
    for contract in (native, policy, requests):
        if contract["schemaVersion"] != 1 or contract["dependencies"] != policy["dependencies"]:
            raise ValueError("Inconsistent Stride contracts")
        for name, digest in contract["dependencies"].items():
            if sha((root / name).read_bytes()) != digest:
                raise ValueError("Changed Stride dependency: " + name)
    expected_policy = {"feet": [{"ik": "ik_foot_r", "fk": "foot_r", "thigh": "thigh_r"},
                                {"ik": "ik_foot_l", "fk": "foot_l", "thigh": "thigh_l"}],
                       "pelvis": "pelvis", "footRoot": "ik_foot_root", "rk4UpdateRate": 60, "rk4MaxIterations": 4}
    if policy["policies"] != dict.fromkeys(("unarmed", "pistol", "rifle"), expected_policy):
        raise ValueError("Changed actual Stride feet/spring runtime")
    counts = dict.fromkeys(("frames", "hidden", "poseChanged", "bonesChanged", "rootChanged", "rootPresent",
                           "partialAlpha", "zeroAlpha", "zeroSpeed"), 0)
    if any(len(item["traces"]) != 9 for item in (native, before, requests, clock)):
        raise ValueError("Incomplete Stride traces")
    for trace, old, controls, source in zip(native["traces"], before["traces"], requests["traces"], clock["traces"], strict=True):
        identity = (trace["profile"], trace["hz"])
        if any((item["profile"], item["hz"]) != identity for item in (old, controls, source)):
            raise ValueError("Reordered Stride traces")
        active = [i for i, frame in enumerate(source["frames"]) if frame["active"]]
        if [row["frame"] for row in trace["rows"]] != active or [row["frame"] for row in old["rows"]] != active:
            raise ValueError("Changed active frame outputs")
        for frame, control in zip(source["frames"], controls["frames"], strict=True):
            if not frame["active"]:
                if control is not None:
                    raise ValueError("Hidden frame has node controls")
                counts["hidden"] += 1
        for row, old_row in zip(trace["rows"], old["rows"], strict=True):
            output, base = row["output"], old_row["output"]
            control = controls["frames"][row["frame"]]
            if control is None or any(output[key] != base[key] for key in ("curves", "attributes")) or len(output["pose"]) != 81:
                raise ValueError("Invalid Stride controls/data inventory")
            if ("rootMotion" in output) != ("rootMotion" in base):
                raise ValueError("Changed RootMotion presence")
            for atom in output["pose"] + ([output["rootMotion"]] if "rootMotion" in output else []):
                if not all(math.isfinite(value) for key in ("position", "rotation", "scale") for value in atom[key]):
                    raise ValueError("Nonfinite Stride output")
            if control["alpha"] == 0 and output != base:
                raise ValueError("Filtered Stride altered pose/attribute")
            if "rootMotion" in output:
                for key in ("name", "type", "namespace", "bone", "rotation", "scale"):
                    if output["rootMotion"][key] != base["rootMotion"][key]:
                        raise ValueError("Stride altered nontranslation root fields")
            changed = sum(x != y for x, y in zip(output["pose"], base["pose"], strict=True))
            counts["poseChanged"] += bool(changed)
            counts["bonesChanged"] += changed
            counts["rootChanged"] += output.get("rootMotion") != base.get("rootMotion")
            counts["rootPresent"] += "rootMotion" in output
            counts["partialAlpha"] += 0 < control["alpha"] < 1
            counts["zeroAlpha"] += control["alpha"] == 0
            counts["zeroSpeed"] += control["speed"] == 0
            counts["frames"] += 1
    expected = dict(zip(counts, (3528, 252, 2613, 26295, 2349, 3255, 1431, 693, 555), strict=True))
    if counts != expected:
        raise ValueError("Missing material Stride coverage: " + repr(counts))
    if native["assetSha256"] != clock["assetSha256"] or len(native["assetSha256"]) != previous["packages"]:
        raise ValueError("Changed native asset provenance")
    return {**counts, "packages": previous["packages"], "stage": "AfterStride", "controlledInputs": True,
            "production": False, "wholeMain": False,
            "files": {name: {"sha256": sha((root / name).read_bytes()), "bytes": (root / name).stat().st_size}
                      for name in ("stride_policy.json", "stride_requests.json", "stride_native.json")}}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=Path("../GASP58/Content"))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/stride-verification.json"))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_STRIDE_VERIFIED " + " ".join(f"{key}={result[key]}" for key in ("frames", "poseChanged", "bonesChanged", "rootChanged", "rootPresent", "packages")))

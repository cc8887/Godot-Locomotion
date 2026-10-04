"""Root intervals/provider attributes tied to original source and pose fixtures."""
import argparse
import hashlib
import json
import math
from pathlib import Path
from verify_lyra_cycle_pose import verify as verify_pose

sha = lambda data: hashlib.sha256(data).hexdigest()
identity = {"position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]}
def verify(root, content):
    previous = verify_pose(root, content)
    data = (root / "root_motion_native.json").read_bytes()
    native = json.loads(data)
    policy_bytes = (root / "root_motion_policy.json").read_bytes()
    policy = json.loads(policy_bytes)
    if native["schemaVersion"] != 1 or native["rootPolicySha256"] != sha(policy_bytes):
        raise ValueError("Stale RootMotion fixture")
    for contract in (native, policy):
        for name, digest in contract["dependencies"].items():
            if sha((root / name).read_bytes()) != digest:
                raise ValueError("Changed RootMotion dependency: " + name)
    if {key: policy[key] for key in ("schemaVersion", "name", "type", "namespace", "blend", "bone")} != {
        "schemaVersion": 1, "name": "RootMotionDelta", "type": "/Script/Engine.TransformAnimationAttribute",
        "namespace": "bone", "blend": "Blend", "bone": "root"}:
        raise ValueError("Changed original root attribute policy")
    catalog = json.loads((root / "logical_controls/catalog.json").read_bytes())
    entries = []
    for entry in catalog["entries"]:
        clip = json.loads((root / "logical_controls" / entry["file"]).read_bytes())
        if clip["metadata"]["additiveType"] == "AAT_None":
            entries.append((entry, clip))
            track = next(t for t in clip["raw"]["tracks"] if t["bone"] == "root")
            if any(len(track[key]) != clip["raw"]["sampledKeyCount"] for key in ("positions", "rotations", "scales")):
                raise ValueError("Unsupported direct root channel length")
    if len(entries) != 189 or [entry["slot"] for entry, _ in entries] != native["slots"]:
        raise ValueError("Incomplete root source inventory")
    if len(native["requests"]) != 3024 or len(native["root"]["rows"]) != 3024:
        raise ValueError("Incomplete interval coverage")
    for index, (request, row) in enumerate(zip(native["requests"], native["root"]["rows"], strict=True)):
        if (request["sequence"], request["case"]) != (index // 16, index % 16) or any(
            row[key] != request[key] for key in ("sequence", "case")):
            raise ValueError("Reordered interval request/output")
        if row["rawSample"] != row["rootSample"]:
            raise ValueError("Native provider unexpectedly sampled compressed data")
        enabled = entries[request["sequence"]][1]["metadata"]["enableRootMotion"]
        if row["present"] != enabled or row["provided"] != (row["extracted"] if enabled else identity):
            raise ValueError("Incorrect native provider presence/delta")
    clocks = json.loads((root / "cycle_layer_native_bits.json").read_bytes())
    authored = json.loads((root / "cycle_layer_pose_native_v2.json").read_bytes())
    frames = present = identities = 0
    if len(native["traces"]) != 9:
        raise ValueError("Incomplete Cycle traces")
    for trace, old, clock in zip(native["traces"], authored["traces"], clocks["traces"], strict=True):
        if (trace["profile"], trace["hz"]) != (old["profile"], old["hz"]) or len(trace["rows"]) != len(old["rows"]):
            raise ValueError("Changed native Cycle trace layout")
        for row, old_row in zip(trace["rows"], old["rows"], strict=True):
            if row["frame"] != old_row["frame"] or any(row["output"][key] != old_row["output"][key]
                for key in ("pose", "curves", "attributes")):
                raise ValueError("Provider altered authored pose/curves/attributes")
            attribute = row["output"].get("rootMotion")
            if attribute:
                if any(attribute[key] != policy[key] for key in ("name", "type", "namespace", "bone")):
                    raise ValueError("Invalid generated attribute identity")
                if not all(math.isfinite(v) for key in ("position", "rotation", "scale") for v in attribute[key]):
                    raise ValueError("Nonfinite generated root delta")
                present += 1
                identities += all(attribute[key] == identity[key] for key in identity)
            frames += 1
    if (frames, present, identities) != (3528, 3255, 299) or len(native["probes"]) != 72:
        raise ValueError("Incomplete generated root presence/probes")
    if len({(p["override"], p["presence"], p["weight"]) for p in native["probes"]}) != 72:
        raise ValueError("Duplicate root attribute probes")
    for path, digest in native["assetSha256"].items():
        file = content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
        if sha(file.read_bytes()) != digest:
            raise ValueError("Changed root source asset")
    return {"sequences": 189, "ranges": 3024, "frames": frames, "present": present, "identity": identities,
            "probes": 72, "packages": previous["packages"], "stage": "ProviderPreWarp", "production": False,
            "wholeMain": False, "files": {name: {"sha256": sha((root / name).read_bytes()), "bytes": (root / name).stat().st_size}
            for name in ("root_motion_policy.json", "root_motion_native.json")}}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=Path("../GASP58/Content"))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/root-motion-verification.json"))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_ROOT_MOTION_VERIFIED sequences=189 ranges=3024 frames=3528 present=3255 identity=299 probes=72 packages=" + str(result["packages"]))

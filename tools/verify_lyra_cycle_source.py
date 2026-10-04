"""Verify immutable Cycle probe provenance, bit encodings and coverage."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path
from locomotion_paths import project_path

from verify_lyra_source_nodes import verify as verify_sources

def sha(data): return hashlib.sha256(data).hexdigest()
def verify_bits(value):
    single = double = 0
    if isinstance(value, list):
        for row in value:
            a, b = verify_bits(row); single += a; double += b
    elif isinstance(value, dict):
        for name, item in value.items():
            if name.endswith("Bits"):
                is_double = isinstance(item, str)
                decoded = struct.unpack("<d" if is_double else "<f", struct.pack("<Q" if is_double else "<I", int(item, 16) if is_double else item))[0]
                numeric = value[name[:-4]]
                expected = numeric if is_double else struct.unpack("<f", struct.pack("<f", numeric))[0]
                if not math.isfinite(decoded) or decoded != expected:
                    raise ValueError("Cycle bit/number mismatch: " + name)
                single += not is_double; double += is_double
            elif isinstance(item, (list, dict)):
                a, b = verify_bits(item); single += a; double += b
    return single, double

def verify(root, content):
    sources = verify_sources(root, content)
    native_bytes = (root / "cycle_source_native_bits.json").read_bytes()
    native = json.loads(native_bytes)
    requested = json.loads((root / "cycle_source_requests.json").read_bytes())
    definitions = json.loads((root / "cycle_source_definitions.json").read_bytes())
    if any(row["schemaVersion"] != 1 for row in (native, requested, definitions)):
        raise ValueError("Invalid Cycle schemas")
    request_sha = sha((root / "cycle_source_requests.json").read_bytes())
    if native["requestSha256"] != request_sha or definitions["requestSha256"] != request_sha:
        raise ValueError("Stale Cycle requests")
    for file, expected in native["dependencies"].items():
        if sha((root / file).read_bytes()) != expected: raise ValueError("Changed Cycle dependency: " + file)
    for path, expected in native["assetSha256"].items():
        package = content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
        if sha(package.read_bytes()) != expected: raise ValueError("Changed Cycle source: " + path)
    if len(native["traces"]) != 9 or len(requested["traces"]) != 9 or len(native["assets"]) != 36:
        raise ValueError("Missing Cycle traces/assets")
    identities, used = set(), set()
    frames = requests = clipped = zero = 0
    for trace, authored in zip(native["traces"], requested["traces"]):
        key = (trace["profile"], trace["hz"])
        if key != (authored["profile"], authored["hz"]) or key in identities:
            raise ValueError("Wrong Cycle trace identity")
        identities.add(key)
        if len(trace["frames"]) != trace["hz"] * 6 or len(trace["frames"]) != len(authored["frames"]):
            raise ValueError("Incomplete Cycle trace")
        for row, frame in zip(trace["frames"], authored["frames"]):
            definition = definitions["assets"][row["asset"]]
            clamp = definitions["clamps"][trace["profile"]]
            if any(row[key] != value for key, value in definition.items()) or any(row[key] != value for key, value in clamp.items()):
                raise ValueError("Dynamic/static Cycle definition mismatch")
            if row["strideType"] != "double": raise ValueError("Wrong original stride type")
            used.add(row["asset"]); frames += 1; requests += len(row["inertia"])
            clipped += row["before"] > row["prepared"]; zero += frame["delta"] == 0
    if identities != {(profile, hz) for profile in ("unarmed", "pistol", "rifle") for hz in (30, 60, 120)} or (frames, requests, clipped, len(used)) != (3780, 108, 7, 36):
        raise ValueError("Missing Cycle coverage")
    single, double = verify_bits(native)
    return {"scope": "registeredLinkedCycleCallbackAndSourceUpdateWithIsolatedNativeSync",
            "productionHost": False, "wholeGraph": False, "notifyConsumer": False,
            "packages": sources["packages"], "dependencies": sources["logicalDependencies"],
            "clips": 234, "traces": 9, "frames": frames, "assets": len(used), "inertiaRequests": requests,
            "lengthClamps": clipped, "zeroDelta": zero, "binary32Fields": single, "binary64Fields": double,
            "fixtures": {name: {"sha256": sha((root / name).read_bytes()), "bytes": (root / name).stat().st_size}
                         for name in ("cycle_source_requests.json", "cycle_source_native_bits.json", "cycle_source_definitions.json")}}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=project_path('Content'))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/cycle-source-verification.json"))
    args = parser.parse_args(); result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_CYCLE_SOURCE_VERIFIED " + " ".join(f"{key}={result[key]}" for key in
          ("packages", "dependencies", "clips", "traces", "frames", "assets", "inertiaRequests", "lengthClamps", "binary32Fields", "binary64Fields")))

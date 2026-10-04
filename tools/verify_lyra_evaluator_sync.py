"""Verify source provenance and immutable native evaluator/Sync trace coverage."""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path
from locomotion_paths import project_path

from verify_lyra_source_nodes import verify as verify_sources


def sha(data):
    return hashlib.sha256(data).hexdigest()


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


def verify_bits(value):
    count = 0
    if isinstance(value, list):
        return sum(verify_bits(row) for row in value)
    if not isinstance(value, dict):
        return 0
    for key, field in value.items():
        if key.endswith("Bits"):
            if not isinstance(field, int) or not 0 <= field <= 0xffffffff:
                raise ValueError("Invalid native binary32 field: " + key)
            actual = struct.unpack("<f", struct.pack("<I", field))[0]
            if not math.isfinite(actual) or actual != f32(value[key[:-4]]):
                raise ValueError("Native binary32/number mismatch: " + key)
            count += 1
        elif isinstance(field, (list, dict)):
            count += verify_bits(field)
    return count


def verify(root, content):
    sources = verify_sources(root, content)
    result = {"scope": "standaloneUpdateAndSharedSync", "packages": sources["packages"],
              "dependencies": sources["logicalDependencies"],
              "clips": len(json.loads((root / "logical_controls/catalog.json").read_bytes())["entries"]),
              "uniqueSources": sources["logicalSources"], "fixtures": {}}
    for variant in ("sync", "nonloop"):
        request_file = f"evaluator_{variant}_requests.json"
        file = f"evaluator_{variant}_native_bits.json"
        data = (root / file).read_bytes()
        native = json.loads(data)
        requests = json.loads((root / request_file).read_bytes())
        if native["schemaVersion"] != 2 or requests["schemaVersion"] != 1:
            raise ValueError("Unsupported evaluator trace schema")
        for key, dependency in [("requestSha256", request_file), ("sourceNodesSha256", "source_nodes.json"),
                                ("catalogSha256", "logical_controls/catalog.json"),
                                ("calibrationSha256", "logical_controls/calibration.json")]:
            if native[key] != sha((root / dependency).read_bytes()):
                raise ValueError("Stale evaluator dependency: " + dependency)
        if len(native["assets"]) != 4 or len(native["traces"]) != 27 or len(requests["traces"]) != 27:
            raise ValueError("Incomplete evaluator traces")
        if [a["path"] for a in native["assets"]] != requests["assets"]:
            raise ValueError("Evaluator assets differ from requests")
        for path, expected in native["assetSha256"].items():
            package = content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
            if sha(package.read_bytes()) != expected:
                raise ValueError("Changed evaluator source package: " + path)
        frames = ticks = empty = boundary = 0
        for trace, requested in zip(native["traces"], requests["traces"]):
            if trace["scenario"] != requested["scenario"] or trace["hz"] != requested["hz"] or len(trace["frames"]) != len(requested["frames"]):
                raise ValueError("Evaluator trace identity/length differs")
            for frame, authored in zip(trace["frames"], requested["frames"]):
                if frame["delta"] != f32(authored["delta"]) or len(frame["inputs"]) != len(authored["inputs"]):
                    raise ValueError("Evaluator trace inputs differ")
                for observed, input_row in zip(frame["inputs"], authored["inputs"]):
                    if any(observed[key] != value for key, value in input_row.items()):
                        raise ValueError("Evaluator authored input was changed")
                    if variant == "nonloop" and observed["looping"]:
                        raise ValueError("Nonloop evaluator coverage is looping")
                if [i["slot"] for i in frame["inputs"]] != [o["slot"] for o in frame["outputs"]]:
                    raise ValueError("Evaluator output occurrence identity differs")
                boundary += sum(-1 in (o["marker"]["previous"], o["marker"]["next"]) for o in frame["outputs"])
                frames += 1
                ticks += len(frame["outputs"])
                empty += not frame["inputs"]
        if frames != 4725 or ticks != 13608 or empty != 189 or (variant == "nonloop" and boundary == 0):
            raise ValueError("Incomplete evaluator frame/occurrence/boundary coverage")
        result["fixtures"][variant] = {"file": file, "sha256": sha(data), "bytes": len(data),
            "traces": len(native["traces"]), "frames": frames, "ticks": ticks, "empty": empty,
            "boundaries": boundary, "exactFields": verify_bits(native)}
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=project_path('Content'))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/evaluator-sync-verification.json"))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_EVALUATOR_SYNC_VERIFIED " + " ".join(f"{key}={result[key]}" for key in ("packages", "dependencies", "clips")) +
          " traces=54 frames=9450 ticks=27216 exactFields=" + str(sum(f["exactFields"] for f in result["fixtures"].values())))

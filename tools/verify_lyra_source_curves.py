"""Verify curve/attribute resource provenance; runtime native values use the Godot smoke."""
import argparse
import json
from pathlib import Path
from locomotion_paths import project_path

from verify_lyra_source_nodes import sha, verify


def verify_curves(root, content):
    source_report = verify(root, content)
    logical = root / "logical_controls"
    catalog_bytes = (logical / "catalog.json").read_bytes()
    calibration_bytes = (logical / "calibration.json").read_bytes()
    bank_bytes = (logical / "curve_bank.json").read_bytes()
    bank = json.loads(bank_bytes)
    if bank["schemaVersion"] != 1 or bank["catalogSha256"] != sha(catalog_bytes) or bank["calibrationSha256"] != sha(calibration_bytes):
        raise ValueError("Stale curve/pose binding")
    catalog = {r["slot"]: r for r in json.loads(catalog_bytes)["entries"]}
    entries = {r["slot"]: r for r in bank["entries"]}
    if len(entries) != len(bank["entries"]) or set(entries) != set(catalog):
        raise ValueError("Incomplete source curve closure")
    for slot, row in entries.items():
        if row["source"] != catalog[slot]["source"]:
            raise ValueError("Wrong source curve identity: " + slot)
        if row["transformCurves"] != 0:
            raise ValueError("Unsupported transform curve: " + slot)
        for attribute in row["attributes"]:
            if attribute["type"] != "/Script/Engine.IntegerAnimationAttribute" or attribute["bone"].lower() != "pelvis" or attribute["boneIndex"] != 1:
                raise ValueError("Unexpected source attribute operator/bone: " + slot)
    native_bytes = (logical / "curve_native.json").read_bytes()
    native = json.loads(native_bytes)
    if native["schemaVersion"] != 1 or native["curveBankSha256"] != sha(bank_bytes):
        raise ValueError("Stale native source curve reference")
    fixtures = {r["slot"] for r in bank["fixtures"]}
    if fixtures != {"fixture_base", "fixture_additive"} or {r["slot"] for r in native["rows"]} != set(entries) | fixtures:
        raise ValueError("Incomplete native source coverage")
    return {"scope": "rawAndAdditiveSourceResources", "packages": source_report["packages"],
            "dependencies": source_report["logicalDependencies"], "clips": len(entries),
            "curves": sum(len(r["curves"]) for r in entries.values()),
            "curveNames": sorted({c["name"] for r in entries.values() for c in r["curves"]}),
            "attributes": sum(len(r["attributes"]) for r in entries.values()),
            "fixtures": len(fixtures), "nativeRows": len(native["rows"]),
            "curveBankSha256": sha(bank_bytes), "curveBankBytes": len(bank_bytes),
            "nativeSha256": sha(native_bytes), "nativeBytes": len(native_bytes)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=project_path('Content'))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/source-curves-verification.json"))
    args = parser.parse_args()
    result = verify_curves(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_SOURCE_CURVES_VERIFIED " + " ".join(f"{key}={result[key]}" for key in
          ["packages", "dependencies", "clips", "curves", "attributes", "fixtures", "nativeRows"]))

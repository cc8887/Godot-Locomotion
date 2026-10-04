"""Verify immutable compiled-source metadata and report the static resource closure."""
import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path

def sha(data):
    return hashlib.sha256(data).hexdigest()

def verify(root, content):
    data = (root / "source_nodes.json").read_bytes()
    document = json.loads(data)
    for key, file in [("inventorySha256", "linked_layer_inventory.json"),
                      ("runtimeGraphSha256", "runtime_graph.json"),
                      ("logicalCatalogSha256", "logical_controls/catalog.json")]:
        if document[key] != sha((root / file).read_bytes()):
            raise ValueError("Stale source dependency: " + file)
    calibration_bytes = (root / "logical_controls/calibration.json").read_bytes()
    calibration = json.loads(calibration_bytes)
    packages = dict(calibration["assetSha256"])
    for path, value in document["assetSha256"].items():
        if path in packages and packages[path] != value:
            raise ValueError("Conflicting source package provenance: " + path)
        packages[path] = value
    for path, value in packages.items():
        file = content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
        if sha(file.read_bytes()) != value:
            raise ValueError("Changed source package: " + path)
    logical = json.loads((root / "logical_controls/catalog.json").read_bytes())
    if logical["calibrationSha256"] != sha(calibration_bytes):
        raise ValueError("Stale logical source calibration")
    for file, value in calibration["dependencySha256"].items():
        if sha((root / file).read_bytes()) != value:
            raise ValueError("Changed logical source dependency: " + file)
    for entry in logical["entries"]:
        if sha((root / "logical_controls" / entry["file"]).read_bytes()) != entry["sha256"]:
            raise ValueError("Changed logical clip: " + entry["slot"])
    runtime = json.loads((root / "runtime_graph.json").read_bytes())
    exported = {entry["source"] for entry in logical["entries"]}
    report = {"scope": "compiledDefaultsAndBlendSpaceSamples", "sourceNodesSha256": sha(data),
              "packages": len(packages), "sourceInventoryPackages": len(document["assetSha256"]),
              "logicalDependencies": len(calibration["dependencySha256"]), "logicalSources": len(exported), "classes": {}}
    for name, row in document["classes"].items():
        nodes = {node["nodeIndex"]: node for node in row["sources"]}
        if len(nodes) != len(row["sources"]):
            raise ValueError("Duplicate source ID")
        for node in nodes.values():
            if node["nodeIndex"] + node["propertyIndex"] != row["nodeCount"] - 1 or node["nativeNodeIndex"] != node["propertyIndex"]:
                raise ValueError("Wrong node/property index")
        owners = {}
        for graph in row["graphs"]:
            if len(set(graph["players"])) != len(graph["players"]):
                raise ValueError("Duplicate graph source")
            for index in graph["players"]:
                if index not in nodes or index in owners:
                    raise ValueError("Invalid graph ownership")
                owners[index] = graph["name"]
        bound, absent = {}, {}
        for node in nodes.values():
            key = f"{owners.get(node['nodeIndex'], '<unharvested>')}:{node['nodeIndex']}"
            assets = ([node["asset"]] if node["kind"] != "BlendSpacePlayer" else
                      [sample["animation"] for sample in node["samples"]])
            for asset in assets:
                if not asset:
                    continue
                (bound if asset in exported else absent).setdefault(asset, []).append(key)
        report["classes"][name] = {"sources": len(nodes), "kinds": dict(Counter(n["kind"] for n in nodes.values())),
            "dynamicAssetNodes": sum(not n["asset"] for n in nodes.values()),
            "callbacks": len(row["callbacks"]), "groups": dict(Counter((n["group"] + ":" + str(n["role"])) for n in nodes.values())),
            "graphs": {g["name"]: g["players"] for g in row["graphs"]},
            "stateSources": {m["machineName"]: {s["stateName"]: s["playerNodeIndices"] for s in m["states"]}
                             for m in runtime["classes"][name]["machines"]},
            "unharvestedNodes": sorted(set(nodes) - set(owners)),
            "exportedStaticSources": bound, "missingStaticSources": absent}
    return report

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=Path("../GASP58/Content"))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/source-nodes-verification.json"))
    args = parser.parse_args()
    result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    count = sum(row["sources"] for row in result["classes"].values())
    for name in ["main", "unarmed", "pistol", "rifle"]:
        row = result["classes"][name]
        print(f"LYRA_SOURCE_CLOSURE profile={name} exported={len(row['exportedStaticSources'])} missing={len(row['missingStaticSources'])} dynamicNodes={row['dynamicAssetNodes']}")
    print(f"LYRA_SOURCE_NODES_VERIFIED classes={len(result['classes'])} sources={count} packages={result['packages']}")

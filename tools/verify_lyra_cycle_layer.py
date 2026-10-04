"""Verify real compiled Cycle closure, source traces and immutable provenance."""
import argparse
import hashlib
import json
from pathlib import Path
from verify_lyra_cycle_source import verify_bits
from verify_lyra_source_nodes import verify as verify_sources

def sha(data): return hashlib.sha256(data).hexdigest()
def verify(root, content):
    sources = verify_sources(root, content)
    contract_bytes = (root / "cycle_layer_graph.json").read_bytes(); contract = json.loads(contract_bytes)
    source_nodes = json.loads((root / "source_nodes.json").read_bytes())
    if contract["schemaVersion"] != 1 or contract["sourceNodesSha256"] != sha((root / "source_nodes.json").read_bytes()) or len(contract["graphs"]) != 9:
        raise ValueError("Stale Cycle graph closure")
    for profile, graph in contract["graphs"].items():
        owner = source_nodes["classes"][profile]; count = owner["nodeCount"]
        layer = next(l for l in owner["layers"] if l["name"] == "FullBody_CycleState")
        if graph["rootPropertyIndex"] != layer["rootIndex"] or graph["root"] != count - 1 - graph["rootPropertyIndex"] or len(graph["nodes"]) != 8:
            raise ValueError("Wrong original root/property mapping")
        inventory = {n["index"]: n for n in graph["nodes"]}
        for node in inventory.values():
            for link in node["links"]:
                if link["index"] not in inventory or link["propertyIndex"] != count - 1 - link["index"]:
                    raise ValueError("Wrong compiled Cycle link")
        leaves = [n for n in inventory.values() if not n["links"]]
        if {n["index"] for n in leaves} != {49, 51}: raise ValueError("Wrong Cycle source closure")
    fixtures = {}; result = {"scope": "originalCycleRootSourceTraversalAndCommonSync", "graphs": 9, "compiledNodes": 72,
        "packages": sources["packages"], "dependencies": sources["logicalDependencies"], "clips": 234,
        "productionHost": False, "warpPoseEvaluated": False, "wholeMain": False, "notifyConsumer": False}
    for variant in ("cycle_layer", "cycle_layer_hidden_reset"):
        native_name, request_name = variant + "_native_bits.json", variant + "_requests.json"
        native = json.loads((root / native_name).read_bytes()); requests = json.loads((root / request_name).read_bytes())
        if native["schemaVersion"] != 1 or native["requestSha256"] != sha((root / request_name).read_bytes()) or native["contractSha256"] != sha(contract_bytes):
            raise ValueError("Stale Cycle layer fixture")
        for name, expected in native["dependencies"].items():
            if sha((root / name).read_bytes()) != expected: raise ValueError("Changed Cycle layer dependency: " + name)
        for path, expected in native["assetSha256"].items():
            package = content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")
            if sha(package.read_bytes()) != expected: raise ValueError("Changed native Cycle package: " + path)
        if len(native["traces"]) != 9 or len(requests["traces"]) != 9 or len(native["assets"]) != 42:
            raise ValueError("Incomplete Cycle layer trace inventory")
        identities = set(); frames = cycle = hipfire = hidden = resets = tiny = inertia = 0
        for trace, authored in zip(native["traces"], requests["traces"]):
            key = (trace["profile"], trace["hz"])
            if key != (authored["profile"], authored["hz"]) or key in identities or len(trace["frames"]) != trace["hz"] * 6 or len(trace["frames"]) != len(authored["frames"]):
                raise ValueError("Wrong Cycle trace identity")
            identities.add(key)
            for row, frame in zip(trace["frames"], authored["frames"]):
                if row["active"] != frame["active"] or row["strideType"] != "double": raise ValueError("Wrong Cycle root input/type")
                frames += 1; cycle += row["active"]; hipfire += row["hipFireActive"]; hidden += not row["active"]
                resets += frame["reinitialize"] and not frame["active"]
                tiny += row["hipFireActive"] and row["hipFireWeight"] <= 1e-5; inertia += len(row["inertia"])
                if row["hipFireActive"]:
                    if row["hipFireAsset"] not in authored["bindings"].values() or row["hipFireExplicit"] != 0:
                        raise ValueError("Wrong original HipFire callback binding/time")
        if (frames, cycle, hipfire, hidden, tiny, inertia) != (3780, 3528, 2205, 252, 225, 108) or resets != (18 if "hidden_reset" in variant else 0):
            raise ValueError("Missing Cycle traversal/reinitialization coverage")
        singles, doubles = verify_bits(native)
        fixtures[variant] = {"frames": frames, "cycleTicks": cycle, "hipFireTicks": hipfire, "hidden": hidden,
            "hiddenResets": resets, "tinyWeightedTicks": tiny, "inertia": inertia, "binary32Fields": singles, "binary64Fields": doubles,
            "files": {name: {"sha256": sha((root / name).read_bytes()), "bytes": (root / name).stat().st_size} for name in (native_name, request_name)}}
    result["contractSha256"] = sha(contract_bytes); result["fixtures"] = fixtures
    return result

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("assets/generated/lyra_als"))
    parser.add_argument("--content", type=Path, default=Path("../GASP58/Content"))
    parser.add_argument("--out", type=Path, default=Path("artifacts/lyra-analysis/cycle-layer-verification.json"))
    args = parser.parse_args(); result = verify(args.root, args.content)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("LYRA_CYCLE_LAYER_VERIFIED graphs=9 nodes=72 traces=18 frames=7560 cycle_ticks=7056 hipfire_ticks=4410 hidden=504 hidden_resets=18 packages=" + str(result["packages"]))

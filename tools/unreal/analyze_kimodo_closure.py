"""Analyze the original six-domain raw source closures against the Kimodo manifest mapping."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "assets/generated/kimodo_als/als_manifest.json"

manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
kimodo = {}
for entry in manifest["animations"]:
    source = entry.get("kimodoSource")
    if source:
        kimodo[entry["objectPath"]] = source["retargetedAssetPath"]
    else:
        kimodo[entry["objectPath"]] = entry["objectPath"]  # kept originals load directly

# original source -> baseAsset from existing original raw sequence files
base_map = {}
raw_dir = ROOT / "assets/config/raw_sequences"
for path in sorted(raw_dir.glob("*.json")):
    row = json.loads(path.read_text(encoding="utf-8"))
    base = row.get("evaluation", {}).get("baseAsset")
    base_map[row["source"]] = base

print("manifest animations:", len(manifest["animations"]), "kimodo mapped:", len(kimodo))
print("original raw sequence files:", len(base_map))

domains = ["recovery_movement", "aim", "overlay", "ragdoll", "stop", "overlay_prop"]
all_unmapped = set()
for domain in domains:
    index_path = ROOT / ("assets/config/v4_%s_source_inputs.json" % domain)
    if not index_path.is_file():
        print(domain, "INDEX MISSING", index_path)
        continue
    index = json.loads(index_path.read_text(encoding="utf-8"))
    sources = [entry["source"] for entry in index["assets"]]
    unmapped = [s for s in sources if s not in kimodo]
    bases = {base_map[s] for s in sources if base_map.get(s)}
    base_unmapped = [b for b in sorted(bases) if b not in kimodo]
    print("%s assets=%d unmapped=%d bases=%d base_unmapped=%d" %
          (domain, len(sources), len(unmapped), len(bases), len(base_unmapped)))
    for s in unmapped:
        print("  UNMAPPED", s)
        all_unmapped.add(s)
    for b in base_unmapped:
        print("  BASE_UNMAPPED", b)
        all_unmapped.add(b)

    request_path = ROOT / ("artifacts/kimodo_als/%s-request.json" % domain)
    if request_path.is_file():
        request = json.loads(request_path.read_text(encoding="utf-8"))
        missing = [r["source"] for r in request["rootAssets"] if r["source"] not in kimodo]
        print("  request roots=%d players=%d samples=%d missing=%d" %
              (len(request["rootAssets"]), request["players"], request["samples"], len(missing)))
        for m in missing:
            print("    REQ_MISSING", m)

print("TOTAL unique unmapped across domains:", len(all_unmapped))
for s in sorted(all_unmapped):
    print("  TOTAL_UNMAPPED", s)

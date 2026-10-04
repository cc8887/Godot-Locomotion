"""Check immutable native Aiming-weight fixture, source provenance and process gates."""
import argparse
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

parser = argparse.ArgumentParser()
parser.add_argument('--ue-log', action='append', required=True)
parser.add_argument('--godot-log', required=True)
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
native = json.loads((root / 'aim_weight_v1_native.json').read_bytes())
assert native['schemaVersion'] == 1
assert sha(root / 'aim_weight_v1_requests.json') == native['requestSha256']
assert sha(root / 'aim_weight_v1_policy.json') == native['policySha256']
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
for name, digest in native['assetSha256'].items():
    assert sha(project_path('Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
for name, digest in native['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / name) == digest, name
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / name) == digest, (tree, name)
for path in args.ue_log:
    log = Path(path).read_text(encoding='utf-8', errors='replace')
    assert log.count('LYRA_AIM_WEIGHT_NATIVE_OK frames=11340') == 1, path
    assert log.count('LYRA_EXPORT_PROCESS_EXIT_OK mode=aim-weight code=0') == 1, path
    assert '0 error(s)' in log and 'Traceback' not in log and 'LYRA_AIM_WEIGHT_FAILED' not in log, path
log = Path(args.godot_log).read_text(encoding='utf-8', errors='replace')
assert log.count('LYRA_AIM_WEIGHT_GODOT_OK frames=11340') == 1
assert 'ERROR:' not in log and 'WARNING:' not in log
print('LYRA_AIM_WEIGHT_VERIFY_OK frames=11340 previous=' + str(len(native['previousFixtureSha256'])) +
      ' packages=' + str(len(native['assetSha256'])) + ' ueProcesses=' + str(len(args.ue_log)) + ' pose=false')

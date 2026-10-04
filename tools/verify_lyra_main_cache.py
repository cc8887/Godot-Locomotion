"""Check immutable native evidence and actual process results for Main caches."""
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
native = json.loads((root / 'main_cache_v1_native.json').read_bytes())
assert native['schemaVersion'] == 1
assert native['requestSha256'] == sha(root / 'main_cache_v1_requests.json')
assert native['policySha256'] == sha(root / 'main_cache_v1_policy.json')
for name, digest in native['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in native['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
for name, digest in native['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
for name, digest in native['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / name) == digest, name
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / name) == digest, (tree, name)
assert {(t['profile'], t['hz']) for t in native['traces']} == {(p, h) for p in ('unarmed', 'pistol', 'rifle') for h in (30, 60, 120)}
assert native['counts']['frames'] == 2520
for trace in native['traces']:
    assert trace['mainOrder'] == [78, 83] and trace['providerOrder'] == [78]
for name in ('main-cache-ue-export2.log', 'main-cache-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8')
    assert text.count('LYRA_MAIN_CACHE_NATIVE_OK frames=2520') == 1, name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-cache code=0' in text, name
    assert 'Success - 0 error(s)' in text, name
    assert 'Handled ensure' not in text and ': Error:' not in text, name
for name, marker, mode in (
    ('main-cache-godot.log', 'LYRA_MAIN_CACHE_GODOT_OK frames=2520', 'cache'),
    ('main-cache-composition-regression.log', 'LYRA_MAIN_COMPOSITION_SCOPE_GODOT_OK frames=11340 poses=9762', 'composition'),
    ('main-cache-main-regression.log', 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595', 'main'),
):
    text = (logs / name).read_text(encoding='utf-8')
    assert marker in text and 'ERROR:' not in text and 'WARNING:' not in text, name
    assert f'LYRA_TEST_PROCESS_EXIT mode={mode} code=0' in text, name
scope = (logs / 'main-cache-composition-regression.log').read_text(encoding='utf-8')
assert 'cacheUpdateTraversal=true cachePoseEvaluation=false' in scope
assert 'groupEntries=14 commonSync=true' in scope
assert 'inertia=false controlRig=false nativeCombined=false production=false' in scope
for name in ('main-cache-debug3.log', 'main-cache-optimize.log'):
    text = (logs / name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text, name
print('LYRA_MAIN_CACHE_FINAL_VERIFIED frames=2520 updates=' + str(native['counts']['updates']) +
      ' packages=' + str(len(native['assetSha256'])) + ' previous=' + str(len(native['previousFixtureSha256'])) +
      ' groupEntries=14 cacheUpdate=true cachePoseEvaluation=false inertia=false production=false whole_goal=false')

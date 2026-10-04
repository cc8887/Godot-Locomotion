"""Verify original graph inventory and typed group execution evidence."""
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
data = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
assert data['schemaVersion'] == 1
for name, digest in data['dependencies'].items():
    assert sha(root / name) == digest, name
for name, digest in data['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
assert len(data['previousFixtureSha256']) == 645
for path, digest in data['assetSha256'].items():
    p = project_path('Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(p) == digest, path
assert len(data['assetSha256']) == 508
for name, digest in data['probeSourceSha256'].items():
    for tree in (repo / 'tools/unreal/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo / 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree / 'Source/AlsV4AssetExporter' / name) == digest, (tree, name)
contracts = json.loads((root / 'linked_layer_contracts.json').read_bytes())
graphs = 0
for profile, cls in data['classes'].items():
    original = contracts['classes'][profile]
    assert cls['classPath'] == original['class']
    expected = {'AnimGraph'} if profile == 'main' else {f['name'] for f in original['functions'] if f['implemented']}
    assert set(cls['graphs']) == expected
    for name, graph in cls['graphs'].items():
        nodes = {n['index']: n for n in graph['nodes']}
        assert len(nodes) == len(graph['nodes']) and graph['root'] in nodes
        assert nodes[graph['root']]['settings']['name'] == name
        assert all(link['index'] in nodes for n in nodes.values() for link in n['links'])
        graphs += 1
assert graphs == 43
main = data['classes']['main']['graphs']['AnimGraph']
assert len(main['nodes']) == 49
calls = {n['settings']['layer']: n['index'] for n in main['nodes'] if n['type'].endswith('.AnimNode_LinkedAnimLayer')}
assert len(calls) == 14 and calls['FullBody_CycleState'] == 15 and calls['FullBody_SkeletalControls'] == 4
for profile in ('unarmed', 'pistol', 'rifle'):
    fields = data['classes'][profile]['defaults']['fields']
    assert fields['EnableLeftHandPoseOverride'] == {'type': 'bool', 'value': False}
    assert fields['LeftHandPose_Override'] == {'type': 'UAnimSequence*', 'value': ''}
for name in ('main-layer-graph-ue-export-first.log', 'main-layer-graph-ue-export-repeat.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count('LYRA_MAIN_LAYER_GRAPH_OK classes=4 hooks=43 packages=508 previous=645 assets_saved=0') == 1, name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=main-layer-graph code=0' in text, name
    assert 'Success - 0 error(s), 692 warning(s)' in text, name
    assert 'Traceback' not in text and 'LogWindows: Error:' not in text, name
for name, marker in (
    ('item-layer-execution-own-final.log', 'LYRA_MAIN_OWN_LOCOMOTION_OK frames=7560 poses=6330'),
    ('item-layer-execution-native-final.log', 'LYRA_ITEM_LAYER_EXECUTION_OK frames=11340 calls=12595 entries=10 contractEntries=14 rejected=510'),
):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert text.count(marker) == 1 and 'ERROR:' not in text and 'WARNING:' not in text, name
native = (logs / 'item-layer-execution-native-final.log').read_text(encoding='utf-8')
assert 'LYRA_MAIN_ALS_NATIVE_GODOT_OK frames=11340 poses=9762 rootPoses=12595' in native
assert 'nativeJoint=true boundary=LocomotionSM fixedProvider=true finalLayers=false production=false' in native
for name in ('item-layer-execution-debug-final.log', 'item-layer-execution-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8', errors='replace')
    assert '0 个警告' in text and '0 个错误' in text, name
print('LYRA_ITEM_LAYER_EXECUTION_FINAL_VERIFIED graphs=43 mainNodes=49 contractEntries=14 executedEntries=10 '
      'frames=11340 calls=12595 rejected=510 packages=508 previous=645 nativeJoint=true production=false whole_goal=false')

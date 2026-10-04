"""Read the actual FullBodyAdditives machine and its compiled rules on ALS81."""
import hashlib
import json
import os
import struct
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'additives_layer_v1'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
f32 = lambda v: struct.unpack('<f', struct.pack('<f', v))[0]
graph = json.loads((root / 'main_layer_graph_v1.json').read_bytes())
packages = graph['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p) for p in root.rglob('*.json') if not p.name.startswith(prefix)}
if (root / (prefix + '_native.json')).exists():
    previous = json.loads((root / (prefix + '_native.json')).read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for name, digest in packages.items():
        assert sha(content / (name.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, name
    for name, digest in previous.items():
        assert sha(root / name) == digest, name

def save(kind, data):
    p = root / (prefix + '_' + kind + '.json')
    if p.exists():
        assert json.loads(p.read_bytes()) == data, 'Immutable additive fixture differs: ' + kind
    else:
        p.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
    return sha(p)

protect()
dependencies = {p: sha(root / p) for p in ('main_layer_graph_v1.json', 'runtime_graph.json',
    'pose_layer_contracts.json', 'logical_controls/calibration.json', 'locomotion_resources.json',
    'locomotion_extras/catalog.json','locomotion_extras/playback.json')}
basis = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
skeleton = unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(
    unreal.load_asset(basis['sourceMesh']).get_editor_property('skeleton'),
    unreal.load_asset(basis['targetMesh']).get_editor_property('skeleton'), unreal.Quat(*basis['handBasis']['rotation']))
extras=json.loads((root/'locomotion_extras/catalog.json').read_bytes())
playback=json.loads((root/'locomotion_extras/playback.json').read_bytes())
recovery={r['profile']:r for r in extras['entries'] if r['additive']}
sequences=[];resource_rows={}
for profile,row in recovery.items():
    sequence=unreal.AlsLyraControlRigLibrary.create_weapon_sequence(unreal.load_asset(row['source']),
        unreal.load_asset(row['target']),skeleton,unreal.Quat(*basis['handBasis']['rotation']),None)
    assert sequence is not None
    unreal.AlsSourceAnimationLibrary.finish_source_compression(sequence)
    sequences.append(sequence)
    sync=json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(sequence))
    assert not sync['markers'],sync
    resource_rows[profile]=dict(slot=row['slot'],path=row['target'],sync=sync,
        compressedRoot=json.loads(unreal.AlsLyraGraphLibrary.read_root_compression_data(sequence)))
requests = dict(traces=[],sequencePaths=[r['target'] for r in recovery.values()])
for profile in ('unarmed', 'pistol', 'rifle'):
    for hz in (30, 60, 120):
        frames = []
        for i in range(hz * 12):
            t = i / hz
            frames.append(dict(delta=f32(0 if i % 101 == 100 else 1 / hz),
                ground=int(t / .75) % 2 == 1, IsFalling=int(t/.75)%2==0 and int(t/.125)%3!=0,
                IsJumping=int(t/.75)%2==0 and int(t/.125)%3==0, IsCrouching=int(t/1.1)%2==1,
                visited=not (.8 <= t < 1.1 or 4.7 <= t < 4.9 or 9 <= t < 9.25),
                evaluate=i % 7 != 3, active=i % 41 < 35, initialize=i == 0 or i % 83 == 0,
                weight=f32([1, .5, 1e-5, .001, .8][(i // 19) % 5])))
        requests['traces'].append(dict(profile=profile,hz=hz,**{'class':graph['classes'][profile]['classPath']},
            bindings={'Jump_RecoveryAdditive':recovery[profile]['target']},frames=frames))
native = json.loads(unreal.AlsLyraAdditivesLibrary.read_trace(
    unreal.load_class(None,graph['classes']['main']['classPath']),unreal.load_asset(basis['sourceMesh']),skeleton,
    sequences,json.dumps(requests,separators=(',', ':'))))
diagnostic = repo / 'artifacts/lyra-analysis/additives-layer-native-sync-diagnostic.json'
if not diagnostic.exists():
    diagnostic.write_text(json.dumps(native,separators=(',', ':')),encoding='utf-8')
counts = dict(frames=0,poses=0,hidden=0,updateOnly=0,transitions=0,initializations=0,inactive=0,zeroDelta=0)
states = set()
for trace, request in zip(native['traces'],requests['traces'],strict=True):
    for i,(row, frame) in enumerate(zip(trace['frames'],request['frames'],strict=True)):
        assert {r['edge']:r['result'] for r in row['rules']} == {0:not frame['ground'],1:frame['ground'],2:not frame['ground']}, (trace['profile'],trace['hz'],i,frame['ground'],row['rules'])
        assert [r['delegate'] for r in row['rules']] == [8,9,10]
        assert row['after']['state'] in (0,1,2) and len(row['initializations']) == 3
        assert ('output' in row) == (frame['visited'] and frame['evaluate'])
        if 'output' in row:
            output = row['output']
            assert len(output['pose']) == 81
        counts['frames'] += 1
        counts['poses'] += 'output' in row
        counts['hidden'] += not frame['visited']
        counts['updateOnly'] += frame['visited'] and not frame['evaluate']
        counts['transitions'] += row['before']['state'] != row['after']['state']
        counts['initializations'] += sum(row['initializations'])
        counts['inactive'] += frame['visited'] and not frame['active']
        counts['zeroDelta'] += frame['delta'] == 0
        states.add(row['after']['state'])
assert counts['frames'] == 7560 and counts['transitions'] > 0 and states == {0,1,2}, counts
protect()
probe_names = ('Private/AlsLyraAdditivesLibrary.cpp','Public/AlsLyraAdditivesLibrary.h',
               'Private/AlsLyraCyclePoseLibrary.cpp','Private/AlsLyraPoseProbe.h','Private/AlsLyraAirLibrary.cpp',
               'Private/AlsLyraControlRigLibrary.cpp')
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
source_sha = {p:sha(source / p) for p in probe_names}
for tree in ('source','package'):
    mirror = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in source_sha.items():
        assert sha(mirror / p) == digest, (tree,p)
request_sha = save('requests',requests)
policy_sha = save('policy',dict(schemaVersion=1,dependencies=dependencies,stage='OriginalFullBodyAdditives',skeleton='ALS81',
    rules=[dict(edge=0,delegate=8,predicate='NotIsOnGround'),dict(edge=1,delegate=9,predicate='IsOnGround'),dict(edge=2,delegate=10,predicate='NotIsOnGround')],
    automaticEdge=3,landingEdgeEnabled=True,additiveContext=True,resources=resource_rows))
native.update(schemaVersion=1,dependencies=dependencies,requestSha256=request_sha,policySha256=policy_sha,assetSha256=packages,
    previousFixtureSha256=previous,probeSourceSha256=source_sha,counts=counts,
    scope='Actual linked FullBodyAdditives Update/Evaluate and original compiled rules, fixed providers, ALS81, enclosing Main fields controlled; not complete Main or production.')
save('native',native)
unreal.log('LYRA_ADDITIVES_LAYER_NATIVE_OK frames=7560 poses='+str(counts['poses'])+' transitions='+str(counts['transitions'])+' previous='+str(len(previous))+' assets_saved=0')

"""Verify original FullBodyAdditives capture, protection and staged Main execution."""
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

repo=Path(__file__).resolve().parents[1]
root=repo/'assets/generated/lyra_als'
logs=repo/'artifacts/lyra-analysis'
prefix='additives_layer_v1'
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
native=json.loads((root/(prefix+'_native.json')).read_bytes())
requests=json.loads((root/(prefix+'_requests.json')).read_bytes())
policy=json.loads((root/(prefix+'_policy.json')).read_bytes())
assert native['schemaVersion']==policy['schemaVersion']==1
assert sha(root/(prefix+'_requests.json'))==native['requestSha256']
assert sha(root/(prefix+'_policy.json'))==native['policySha256']
assert native['dependencies']==policy['dependencies']
for name,digest in native['dependencies'].items():
    assert sha(root/name)==digest,name
for name,digest in native['previousFixtureSha256'].items():
    assert sha(root/name)==digest,name
assert len(native['previousFixtureSha256'])==658
for name,digest in native['assetSha256'].items():
    assert sha(project_path('Content')/(name.split('.')[0].removeprefix('/Game/')+'.uasset'))==digest,name
assert len(native['assetSha256'])==508
for name,digest in native['probeSourceSha256'].items():
    for tree in (repo/'tools/unreal/AlsV4AssetExporter',
                 repo/'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter',
                 repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'):
        assert sha(tree/'Source/AlsV4AssetExporter'/name)==digest,(tree,name)
assert native['counts']==dict(frames=7560,poses=6075,hidden=474,updateOnly=1011,transitions=201,
                             initializations=318,inactive=1011,zeroDelta=72)
assert policy['stage']=='OriginalFullBodyAdditives' and policy['skeleton']=='ALS81'
assert policy['landingEdgeEnabled'] and policy['additiveContext'] and policy['automaticEdge']==3
assert [r['predicate'] for r in policy['rules']]==['NotIsOnGround','IsOnGround','NotIsOnGround']
assert len(requests['sequencePaths'])==3 and len(native['traces'])==9
states=set();changed=attributes=roots=0
for trace,request in zip(native['traces'],requests['traces'],strict=True):
    assert (trace['profile'],trace['hz'])==(request['profile'],request['hz'])
    resource=policy['resources'][trace['profile']]
    assert resource['slot']==trace['profile']+'_jump_recovery_additive' and not resource['sync']['markers']
    assert request['bindings']=={'Jump_RecoveryAdditive':resource['path']}
    for row,frame in zip(trace['frames'],request['frames'],strict=True):
        assert {r['edge']:r['result'] for r in row['rules']}=={0:not frame['ground'],1:frame['ground'],2:not frame['ground']}
        assert [r['delegate'] for r in row['rules']]==[8,9,10]
        states.add(row['after']['state'])
        expected=row['timeFallingBefore']+frame['delta'] if frame['IsFalling'] else 0 if frame['IsJumping'] else row['timeFallingBefore']
        assert row['timeFalling']==expected
        assert row['source']['asset'] in ('',resource['path'])
        assert ('output' in row)==(frame['visited'] and frame['evaluate'])
        if 'output' in row:
            output=row['output'];assert len(output['pose'])==81 and not output['curves']
            changed+=any(b['position']!=[0,0,0] or b['rotation']!=[0,0,0,1] for b in output['pose'])
            attributes+=len(output['attributes']);roots+='rootMotion' in output
assert states=={0,1,2} and changed==roots==1496 and attributes==5984
for name in ('additives-layer-ue-export-recovery.log','additives-layer-ue-export-recovery-repeat.log'):
    text=(logs/name).read_text(encoding='utf-8',errors='replace')
    assert text.count('LYRA_ADDITIVES_LAYER_NATIVE_OK frames=7560 poses=6075 transitions=201 previous=658 assets_saved=0')==1,name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=additives-layer code=0' in text,name
    assert 'Traceback' not in text and 'Error:' not in text and 'Handled ensure' not in text,name
for name,marker in (
    ('additives-layer-godot-final.log','LYRA_ADDITIVES_LAYER_GODOT_OK frames=7560 poses=6075 transitions=198 automaticReentries=24 retries=7560 rejected=25785'),
    ('additives-main-godot-final.log','LYRA_MAIN_ADDITIVES_PIPELINE_OK frames=11340 poses=9762 recoveryTicks=530 recoveryPoses=456 graphEntries=12'),
    ('additives-main-native-regression.log','entries=10 contractEntries=14 rejected=510'),
    ('additives-main-left-regression.log','entries=10 contractEntries=14 rejected=618'),
):
    text=(logs/name).read_text(encoding='utf-8',errors='replace')
    assert text.count(marker)==1 and 'ERROR:' not in text and 'WARNING:' not in text,name
pipeline=(logs/'additives-main-godot-final.log').read_text(encoding='utf-8')
assert 'entries=10 contractEntries=14 rejected=726' in pipeline
assert 'appliedAfterAiming=false nativeJointBoundary=false production=false' in pipeline
for name in ('additives-layer-debug-final.log','additives-layer-optimize-final.log'):
    text=(logs/name).read_text(encoding='utf-8',errors='replace')
    assert '0 个警告' in text and '0 个错误' in text,name
build=(logs/'additives-layer-ue-build-recovery.log').read_text(encoding='utf-8',errors='replace')
assert 'AutomationTool exiting with ExitCode=0 (Success)' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build
print('LYRA_ADDITIVES_LAYER_FINAL_VERIFIED frames=7560 poses=6075 changed=1496 attributes=5984 rootAttributes=1496 '
      'graphEntries=12 mainFrames=11340 mainPoses=9762 packages=508 previous=658 '
      'nativeLayer=true nativeJointBoundary=false production=false whole_goal=false')

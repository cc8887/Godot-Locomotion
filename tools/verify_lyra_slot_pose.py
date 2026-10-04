"""Verify the original Slot oracle, target profile bindings and actual host runs."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())
native = load('slot_pose_v1_native.json')
requests = load('slot_pose_v1_requests.json')
catalog = load('montage_catalog_v2.json')
assert native['schemaVersion'] == 1 and native['rootMotionMode'] == 3
assert native['requestSha256'] == sha(root/'slot_pose_v1_requests.json')
for section in ('dependencies','previousFixtureSha256'):
    for p,digest in native[section].items(): assert sha(root/p) == digest,p
content = Path('../GASP58/Content')
for p,digest in native['assetSha256'].items():
    assert sha(content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')) == digest,p
source = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for p,digest in native['probeSourceSha256'].items():
    assert sha(source/p) == digest,p
    for tree in ('source','package'):
        assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/p) == digest,(tree,p)
for fixture in ('montage_sampling_v1_native.json','montage_blend_v1_native.json'):
    for p,digest in load(fixture)['probeSourceSha256'].items():
        assert sha(source/p) == digest,(fixture,p)
        for tree in ('source','package'):
            assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/p) == digest,(fixture,tree,p)
frames=poses=rows=profile_poses=normalized=local=mesh=hidden=roots=0
assets=set()
for trace,query in zip(native['traces'],requests['traces'],strict=True):
    assert trace['hz'] == query['hz'] and len(trace['frames']) == len(query['frames'])
    for frame,q in zip(trace['frames'],query['frames'],strict=True):
        frames+=1
        for f in frame['frozen']: assets.add(f['asset'])
        assert [r['slot'] for r in frame['slots']] == list(range(5))
        for r in frame['slots']:
            rows+=1
            assert ('output' in r) == q['sample']
            if not q['sample']: continue
            poses+=1;assert len(r['output']['pose']) == 81
            assert r['sourceEvaluations'] == (r['sourceWeight']>1e-5)
            roots+='rootMotion' in r['output']
            slot=['UpperBody','UpperBodyAdditive','FullBodyAdditivePreAim','AdditiveHitReact','FullBody'][r['slot']]
            matching=[f for f in frame['frozen'] if any(t['name']==slot for t in catalog['assets'][f['asset']]['slots'])]
            profile_poses+=any(f['profile'] for f in matching)
            normalized+=r['totalWeight']>1+1e-5
            local+=any(t['name']==slot and t['segments'][0]['additiveType']==1 for f in matching for t in catalog['assets'][f['asset']]['slots'])
            mesh+=any(t['name']==slot and t['segments'][0]['additiveType']==2 for f in matching for t in catalog['assets'][f['asset']]['slots'])
            hidden+=r['sourceWeight']<=1e-5
assert (frames,rows,poses,profile_poses,normalized,local,mesh,hidden)==(15870,79350,17015,72,246,1187,246,534)
assert assets==set(range(45)) and roots==11710
checks={}
for name in ('lyra-slot-pose-ue.log','lyra-slot-pose-repeat-ue.log'):
    text=(logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert text.count('LYRA_SLOT_POSE_NATIVE_OK frames=15870 poses=17015 assets_saved=0')==1,name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=slot-pose code=0' in text,name
    assert not re.search(r'LogPython: Error|Assertion failed|Ensure condition failed|LYRA_SLOT_POSE_CAPTURE_FAILED',text),name
    checks[name]=dict(exit=0,warnings=len(re.findall('Warning:',text)))
for name in ('slot-pose-debug-evaluator.log','slot-pose-optimize.log'):
    text=(logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text),name
    checks[name]=dict(warnings=0,errors=0)
for name in ('slot-pose-godot-first.log','slot-pose-optimize-godot.log'):
    text=(logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert text.count('LYRA_SLOT_POSE_GODOT_OK frames=15870 slotRows=79350 poses=17015')==1,name
    assert 'rootP=0 rootQ=0 rootS=0 originalNodes=true mainIntegration=false' in text,name
    assert 'LYRA_GODOT_PROCESS_EXIT_OK code=0' in text and not re.search('ERROR:|WARNING:',text),name
    checks[name]=dict(exit=0,rootExact=True)
optimized=(logs/'slot-pose-optimize-godot.log').read_text(encoding='utf-8-sig')
assert 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in optimized
for name in ('GodotALS.dll','Als.Core.dll','Als.Import.dll'):
    assert f'LYRA_OPTIMIZED_ASSEMBLY {name} SHA256={sha(repo/".godot/mono/temp/bin/ExportRelease"/name).upper()}' in optimized
for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name)==sha(logs/'slot-pose-debug-assemblies'/name)
for name,marker in [('slot-pose-main-regression.log','LYRA_MAIN_POSE_HOST_GODOT_OK'),
    ('slot-pose-main-feedback-regression.log','LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK'),
    ('slot-pose-sampling-regression.log','LYRA_MONTAGE_SAMPLING_GODOT_OK'),
    ('slot-pose-resources-regression.log','LYRA_MONTAGE_RESOURCES_GODOT_OK'),
    ('slot-pose-slots-regression.log','LYRA_MONTAGE_SLOTS_GODOT_OK')]:
    text=(logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert text.count(marker)==1 and 'LYRA_GODOT_PROCESS_EXIT_OK code=0' in text and not re.search('ERROR:|WARNING:',text),name
    checks[name]=dict(exit=0)
trx=ET.parse(logs/'slot-pose-core.trx').getroot();ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
counters=trx.find('t:ResultSummary/t:Counters',ns).attrib
assert counters['total']==counters['passed']=='243' and counters['failed']==counters['notExecuted']=='0'
build=(logs/'lyra-slot-pose-build-evaluation-header.log').read_text(encoding='utf-8-sig',errors='replace')
assert 'BUILD SUCCESSFUL' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build and 'error C' not in build
report=dict(frames=frames,rows=rows,poses=poses,profilePoses=profile_poses,normalized=normalized,
    localAdd=local,meshAdd=mesh,hiddenSource=hidden,rootPoses=roots,corePassed=243,
    packages=len(native['assetSha256']),previous=len(native['previousFixtureSha256']),checks=checks,
    mainIntegration=False,production=False,wholeGoal=False)
(logs/'lyra-slot-pose-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LYRA_SLOT_POSE_FINAL_VERIFIED',json.dumps(report),'whole_goal=false')

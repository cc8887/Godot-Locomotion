"""Verify actual time/track oracles, immutable resources and final host runs."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads((root / p).read_bytes())
native = load('montage_sampling_v1_native.json')
policy = load('montage_sampling_v1_policy.json')
requests = load('montage_sampling_v1_requests.json')
queries = load('montage_sampling_v1_track_requests.json')['samples']
rows = load('montage_sampling_v1_tracks.json')['rows']
roots = load('montage_sampling_v1_roots.json')
catalog = load('montage_catalog_v2.json')
assert native['schemaVersion'] == policy['schemaVersion'] == roots['schemaVersion'] == 1
for key, filename in [('requestSha256','requests'), ('trackRequestSha256','track_requests'), ('trackSha256','tracks'), ('rootSha256','roots')]:
    assert native[key] == sha(root / ('montage_sampling_v1_' + filename + '.json'))
assert policy['rootSha256'] == native['rootSha256'] and policy['dependencies'] == native['dependencies']
assert policy['curveCombine'] == 'MontageOverridesSequence' and policy['rootExtraction'] == 'CompressedSequenceTrack'
assert policy['sourceLayout'] == 'ALS81'
assert policy['montagePaths'] == [a['path'] for a in catalog['assets']]
assert policy['sequencePaths'] == list(catalog['sequences'])
for p,digest in native['dependencies'].items():
    assert sha(root/p) == digest,p
for p,digest in native['previousFixtureSha256'].items():
    assert sha(root/p) == digest,p
content = project_path('Content')
for p,digest in native['assetSha256'].items():
    assert sha(content/(p.split('.')[0].removeprefix('/Game/')+'.uasset')) == digest,p
source = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for p,digest in native['probeSourceSha256'].items():
    assert sha(source/p) == digest,p
    for tree in ('source','package'):
        assert sha(repo/'artifacts/unreal/gasp58-lyra-masks'/tree/'AlsV4AssetExporter/Source/AlsV4AssetExporter'/p) == digest,(tree,p)
frame_count = frozen_count = zero = reverse = 0
played = set()
for trace,request in zip(native['traces'],requests['traces'],strict=True):
    assert trace['hz'] == request['hz'] and len(trace['frames']) == len(request['frames'])
    for frame,q in zip(trace['frames'],request['frames'],strict=True):
        frame_count += 1
        for f in frame['frozen']:
            frozen_count += 1; played.add(f['asset']); zero += q['delta'] == 0; reverse += f['delta'] < 0
assert frame_count == 15870 and frozen_count == 14350 and zero == 411 and reverse == 135 and played == set(range(45))
assert len(queries) == len(rows) == 2522
assert {(q['asset'],q['track']) for q in queries} == {(i,t) for i,a in enumerate(catalog['assets']) for t in range(len(a['slots']))}
assert all(len(r['sequence']['pose']) == len(r['output']['pose']) == 81 for r in rows)
assert sum('rootMotion' in r['output'] for r in rows) == 149
assert len(roots['assets']) == 55
entries = load('montage_actions/catalog.json')['entries']
assert set(roots['assets']) == {e['target'] for e in entries}
assert len({k for r in rows for k in r['sequence']['curves']}) == 1
assert {k for r in rows for k in r['output']['curves']} == {'DisableLHandIK','DisableRHandIK','DisableLegIK','ScaleDownWeaponR'}
checks = {}
for name in ('lyra-montage-sampling-ue.log','lyra-montage-sampling-repeat-ue.log'):
    text = (logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert text.count('LYRA_MONTAGE_SAMPLING_NATIVE_OK frames=15870 samples=2522 roots=55 assets_saved=0') == 1,name
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-sampling code=0' in text,name
    assert not re.search(r'LogPython: Error|Assertion failed|Ensure condition failed|LYRA_MONTAGE_SAMPLING_FAILED',text),name
    checks[name] = dict(exit=0,warnings=len(re.findall(r'Warning:',text)))
for name in ('montage-sampling-debug-final.log','montage-sampling-optimize-final.log'):
    text = (logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text),name
    checks[name] = dict(warnings=0,errors=0)
for name in ('montage-sampling-sampling-godot.log','montage-sampling-optimize-godot.log'):
    text = (logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert text.count('LYRA_MONTAGE_SAMPLING_GODOT_OK frames=15870 frozen=14350 retry=15870') == 1,name
    assert 'rootP=0 rootQ=0 rootS=0 exactTime=true' in text,name
    assert 'LYRA_GODOT_PROCESS_EXIT_OK code=0' in text and not re.search(r'ERROR:|WARNING:',text),name
    checks[name] = dict(exit=0,timeExact=True,rootExact=True)
optimized = (logs/'montage-sampling-optimize-godot.log').read_text(encoding='utf-8-sig')
assert 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in optimized
for name in ('GodotALS.dll','Als.Core.dll','Als.Import.dll'):
    assert f'LYRA_OPTIMIZED_ASSEMBLY {name} SHA256={sha(repo/".godot/mono/temp/bin/ExportRelease"/name).upper()}' in optimized
    assert sha(repo/'.godot/mono/temp/bin/Debug'/name) == sha(logs/'montage-sampling-debug-assemblies'/name)
for name,marker in [('montage-sampling-blend-regression-godot.log','LYRA_MONTAGE_BLEND_GODOT_OK'),
    ('montage-sampling-slots-regression-godot.log','LYRA_MONTAGE_SLOTS_GODOT_OK'),
    ('montage-sampling-slots-v1-regression-godot.log','LYRA_MONTAGE_SLOTS_GODOT_OK'),
    ('montage-sampling-resources-regression-godot.log','LYRA_MONTAGE_RESOURCES_GODOT_OK')]:
    text = (logs/name).read_text(encoding='utf-8-sig',errors='replace')
    assert text.count(marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT_OK code=0' in text and not re.search(r'ERROR:|WARNING:',text),name
    checks[name] = dict(exit=0)
trx = ET.parse(logs/'montage-sampling-core-final.trx').getroot()
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
counters = trx.find('t:ResultSummary/t:Counters',ns).attrib
assert counters['total'] == counters['passed'] == '243' and counters['failed'] == counters['notExecuted'] == '0'
build = (logs/'lyra-montage-sampling-build-final.log').read_text(encoding='utf-8-sig',errors='replace')
assert 'BUILD SUCCESSFUL' in build and 'GODOT_ALS_EXTERNAL_EXPORTER_OK' in build and 'error C' not in build
report=dict(frames=frame_count,frozen=frozen_count,samples=len(rows),tracks=60,roots=55,rootSamples=149,
    corePassed=243,packages=len(native['assetSha256']),previous=len(native['previousFixtureSha256']),checks=checks,
    slotPose=False,production=False,wholeGoal=False)
(logs/'lyra-montage-sampling-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LYRA_MONTAGE_SAMPLING_FINAL_VERIFIED',json.dumps(report),'whole_goal=false')

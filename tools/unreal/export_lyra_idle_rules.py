"""Read actual Idle transition handlers independently of state-machine selection."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
sha = lambda data: hashlib.sha256(data).hexdigest()
request = json.loads((root/'idle_runtime_gates_requests.json').read_bytes())
content = Path(unreal.Paths.project_content_dir())
packages = json.loads((root/'idle_runtime_gates_native.json').read_bytes())['assetSha256']
fixtures = {str(p.relative_to(root)).replace('\\','/'):sha(p.read_bytes()) for p in root.rglob('*.json')}
def package_hash(path):
    return sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes())
assert all(package_hash(p) == digest for p,digest in packages.items())
profiles = {t['profile']:t['class'] for t in request['traces']}
fields = ('IsCrouching','GameplayTag_IsADS','GameplayTag_IsFiring','HasVelocity','IsJumping')
traces = []
for profile,cls in profiles.items():
    rows = []
    for bits in range(64):
        main = {k:bool(bits & (1<<i)) for i,k in enumerate(fields)}
        main['RootYawOffset'] = 0
        rows.append(dict(main=main,montage=bool(bits & 32)))
    traces.append(dict(profile=profile,**{'class':cls},rows=rows))
calibration = json.loads((root/'logical_controls/calibration.json').read_bytes())
graph = json.loads((root/'runtime_graph.json').read_bytes())
result = json.loads(unreal.AlsLyraIdleRuleLibrary.read_idle_rules(
    unreal.load_class(None,graph['classes']['main']['class']),
    unreal.load_asset(calibration['calibration']['sourceMesh']),json.dumps(dict(traces=traces))))
for actual,authored in zip(result['traces'],traces,strict=True):
    assert actual['profile'] == authored['profile']
    for row,frame in zip(actual['rows'],authored['rows'],strict=True):
        rules = {(r['machine'],r['edge']):r for r in row['rules']}
        assert rules[1,5]['delegate'] == 33
        assert rules[1,5]['result'] == frame['main']['GameplayTag_IsFiring'], (actual['profile'],frame,rules)
        assert rules[1,7]['result'] == (frame['montage'] or any(frame['main'][k] for k in fields))
        assert not rules[2,0]['result'] and not rules[2,1]['result']
assert all(package_hash(p) == digest for p,digest in packages.items())
assert all(sha((root/p).read_bytes()) == digest for p,digest in fixtures.items())
repo = root.parents[2]
source = repo/'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
payload = dict(schemaVersion=1,requests=dict(traces=traces),result=result,
    packages=packages,fixtures=fixtures,probeSourceSha256={p:sha((source/p).read_bytes()) for p in
        ('Private/AlsLyraIdleRuleLibrary.cpp','Public/AlsLyraIdleRuleLibrary.h')})
path = repo/'artifacts/lyra-analysis/idle-runtime-compiled-rules.json'
if path.exists():
    assert json.loads(path.read_bytes()) == payload
else:
    path.write_text(json.dumps(payload,separators=(',',':')),encoding='utf-8')
unreal.log(f'LYRA_IDLE_COMPILED_RULES_NATIVE_OK cases=192 edge5=firing edge7=anyGate stance=false packages={len(packages)} fixtures={len(fixtures)} assets_saved=0')

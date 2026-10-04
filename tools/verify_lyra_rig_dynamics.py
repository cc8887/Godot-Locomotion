"""Verify the original FootPlant spring/AlphaInterp component boundary."""
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while block := f.read(1024 * 1024): h.update(block)
    return h.hexdigest()


def load(name):
    return json.loads((root / name).read_bytes())


def read(name):
    raw = (logs / name).read_bytes()
    if raw.startswith((b'\xff\xfe', b'\xfe\xff')): return raw.decode('utf-16')
    try: return raw.decode('utf-8-sig')
    except UnicodeDecodeError: return raw.decode('gb18030')


native = load('rig_dynamics_v1_native.json')
requests = load('rig_dynamics_v1_requests.json')
program = load('footplant_rig_inputs_v1_program.json')
assert native['requestSha256'] == sha(root / 'rig_dynamics_v1_requests.json')
assert not native['fullRig'] and not native['production']
for field in ('dependencies', 'previousFixtureSha256'):
    for p, digest in native[field].items(): assert sha(root / p) == digest, p
for p, digest in native['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (p.split('.')[0].removeprefix('/Game/')+'.uasset')) == digest, p
for p, digest in native['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest
assert native['counts'] == dict(traces=9, frames=3360, calls=19217, resets=18, omitted=557,
                               vectorMotion=5492, scalarMotion=8235, alphaMotion=2196,
                               zeroDelta=254, tinyDelta=10, largeDelta=14)
assert [c['instruction'] for c in requests['configs']] == [242,299,311,348,371,317,326]
for config in requests['configs']:
    if config['kind'] == 'alpha':
        assert config['interp'] and not config['map'] and not config['clamp']
        assert config['scale'] == 1 and config['bias'] == 0 and config['increasing'] == config['decreasing'] == 5
    else:
        assert config['useCurrent'] and not config['initializeFromTarget'] and config['damping'] == 1
        assert config['force'] == ([0,0,0] if config['kind'] == 'vector' else 0)
        assert config['strength'] == (8 if config['kind'] == 'vector' else 2.5)
for t, q in zip(native['traces'], requests['traces'], strict=True):
    assert t['name'] == q['name'] and len(t['initial']) == 7
    for r, f in zip(t['frames'], q['frames'], strict=True):
        assert len(r['calls']) == len(f['calls']) and len(r['after']) == 7
        for c, e in zip(f['calls'], r['calls'], strict=True):
            if c['owner'] < 2: assert e['outputVelocity'] == [0,0,0]
checks = {}
for name in ('rig-dynamics-first-ue.log','rig-dynamics-repeat-ue.log'):
    text = read(name)
    assert text.count('LYRA_RIG_DYNAMICS_NATIVE_OK traces=9 frames=3360 calls=19217 assets_saved=0') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=rig-dynamics code=0' in text and ': Error:' not in text
    checks[name] = dict(sha256=sha(logs/name), warnings=len(re.findall(r': Warning:', text)), errors=0)
marker = 'LYRA_RIG_DYNAMICS_GODOT_OK frames=3360 calls=19217 retries=3360 resets=18 rejects=27 comparisons=684328 maxVector=0 maxFloat=0 fullRig=false'
for name in ('rig-dynamics-godot-debug-final.log','rig-dynamics-godot-optimize.log'):
    text = read(name)
    assert text.count(marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT code=0' in text
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    checks[name] = dict(sha256=sha(logs/name), warnings=0, errors=0)
input_marker = 'LYRA_RIG_INPUT_GODOT_OK frames=1260 imports=1077 retries=1260 rejects=6 transforms=1058400 curves=274680 maxP=0 maxQ=0 maxS=0 fullRig=false'
text = read('rig-dynamics-input-debug-regression.log')
assert text.count(input_marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT code=0' in text
assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
checks['rig-dynamics-input-debug-regression.log'] = dict(sha256=sha(logs/'rig-dynamics-input-debug-regression.log'), warnings=0, errors=0)
optimized = read('rig-dynamics-godot-optimize.log')
assert optimized.count('LYRA_OPTIMIZED_ASSEMBLY') == 3 and 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in optimized
assert input_marker in optimized and 'LYRA_INPUT_REGRESSION_PROCESS_EXIT code=0' in optimized
assert 'LYRA_HIERARCHY_REGRESSION_PROCESS_EXIT code=0' in optimized and 'LYRA_CONSTRUCTION_REGRESSION_PROCESS_EXIT code=0' in optimized
for name in ('rig-dynamics-debug-build-final.log','rig-dynamics-optimize-build.log'):
    text = read(name)
    assert re.search(r'0\s*(个警告|Warning)',text) and re.search(r'0\s*(个错误|Error)',text)
    checks[name] = dict(sha256=sha(logs/name),warnings=0,errors=0)
trx = ET.parse(logs/'rig-dynamics-core-regression.trx').getroot()
counters = next(e for e in trx.iter() if e.tag.endswith('Counters'))
assert counters.get('total') == counters.get('passed') == '8' and counters.get('failed') == '0'
bank = load('logical_controls/curve_bank.json')
rig_curve_names = {r['name'] for r in load('footplant_rig_graph_v1.json')['hierarchy'] if 'CURVE' in r['type']}
source_curve_names = {c['name'] for e in bank['entries'] for c in e['curves']}
assert len(bank['entries']) == 234 and not source_curve_names.intersection(rig_curve_names)
assert not any('Curve' in f for f in program['functions'])
sources = ['src/Als.Core/Locomotion/AlsRigVectorSpringModel.cs','src/Als.Godot/Animation/Lyra/LyraFootPlantRigDynamics.cs',
           'src/Als.Godot/Animation/Lyra/LyraRigDynamicsSmoke.cs','tools/prepare_lyra_rig_dynamics.py',
           'tools/unreal/export_lyra_rig_dynamics.py','scripts/export-lyra-rig-dynamics.ps1']
report = dict(stage='OriginalFootPlantDynamicsComponents', accepted=True, fullVmTraversalAccepted=False,
              fullRigPoseAccepted=False, production=False, counts=native['counts'],
              nativeSha256=sha(root/'rig_dynamics_v1_native.json'), protectedPackages=len(native['assetSha256']),
              protectedFixtures=len(native['previousFixtureSha256']), comparisonsPerConfiguration=684328,
              maxVector=0, maxFloat=0, coreRegressionPassed=8,
              curveBoundary=dict(compiledRigCurveFunctions=0, sourceCount=234, sourceCurveNames=sorted(source_curve_names),
                                 rigCurveCount=len(rig_curve_names), sourceIntersection=[], wholeMainCurveInputAccepted=False),
              checks=checks, sources={p:sha(repo/p) for p in sources})
(logs/'lyra-rig-dynamics-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LYRA_RIG_DYNAMICS_VERIFIED components=true frames=3360 calls=19217 exact=true fullVm=false fullRig=false production=false')

"""Verify Main73's bounded ALS input transfer, not the full Rig solver."""
import hashlib
import json
import re
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


def load(name):
    return json.loads((root / name).read_bytes())


def read(name):
    raw = (logs / name).read_bytes()
    if raw.startswith((b'\xff\xfe', b'\xfe\xff')):
        return raw.decode('utf-16')
    try:
        return raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        return raw.decode('gb18030')


native = load('footplant_rig_inputs_v1_native.json')
component = load('footplant_rig_inputs_v1_input.json')
requests = load('footplant_rig_inputs_v1_requests.json')
assert component['nativeSha256'] == sha(root / 'footplant_rig_inputs_v1_native.json')
assert component['requestSha256'] == native['requestSha256'] == sha(root / 'footplant_rig_inputs_v1_requests.json')
assert native['programSha256'] == sha(root / 'footplant_rig_inputs_v1_program.json')
assert native['policySha256'] == sha(root / 'footplant_rig_inputs_v1_policy.json')
for field in ('dependencies', 'previousFixtureSha256'):
    for path, digest in native[field].items():
        assert sha(root / path) == digest, path
for path, digest in native['assetSha256'].items():
    assert sha(Path('../GASP58/Content') / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest
for path, digest in native['probeSourceSha256'].items():
    assert sha(repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter' / path) == digest
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / path) == digest
counts = native['counts']
assert counts == dict(frames=3780, poses=3231, preSolve=3078, preCalls=2001, transferOnly=1077,
                     curves=1571562, partial=164, disabled=149, hidden=315, updateOnly=234, initialize=18)
descriptor = native['traces'][0]['descriptor']
assert descriptor['livePoseAdapterEnabled'] and descriptor['nodeResetInput']
assert not descriptor['nodeTransferGlobal'] and not descriptor['transferLocal']
assert sum(bool(m['rigBone']) for m in descriptor['mapping']) == 69
assert sum(m['requiresSpace'] for m in descriptor['mapping']) == 26
assert len(descriptor['resetBones']) == 22
for t, q in zip(native['traces'], requests['traces'], strict=True):
    assert t['descriptor'] == descriptor and (t['mode'], t['hz']) == (q['mode'], q['hz'])
    for f, r in zip(t['frames'], q['frames'], strict=True):
        expected = r['visited'] and r['evaluate'] and f['alpha'] > .00001
        assert ('preSolve' in f) == expected
        assert f['preCalls'] == (int(expected) if t['mode'] != 'TransferOnly' else 0)
for t, c in zip((t for t in native['traces'] if t['mode'] == 'TransferOnly'), component['traces'], strict=True):
    assert t['hz'] == c['hz'] and t['descriptor'] == c['descriptor']
    assert t['program']['initial']['hierarchy'] == c['initial']
    assert t['frames'][0]['before']['curves'] == c['initialCurves']
    for f, r in zip(t['frames'], c['frames'], strict=True):
        assert r == dict(imported='preSolve' in f, input=f.get('input'), hierarchy=f['after']['hierarchy'], curves=f['after']['curves'])
checks = {}
for name in ('rig-inputs-split-first-ue.log', 'rig-inputs-split-repeat-ue.log'):
    text = read(name)
    assert text.count('LYRA_FOOTPLANT_RIG_INPUTS_NATIVE_OK frames=3780 poses=3231 preSolve=3078 preCalls=2001 transferOnly=1077 assets_saved=0') == 1
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=footplant-rig-inputs code=0' in text and ': Error:' not in text
    checks[name] = dict(sha256=sha(logs / name), warnings=len(re.findall(r': Warning:', text)), errors=0)
assert 'allOtherValuesExact=true originalBytesPreserved=true' in read('rig-inputs-split-repeat-ue.log')
marker = 'LYRA_RIG_INPUT_GODOT_OK frames=1260 imports=1077 retries=1260 rejects=6 transforms=1058400 curves=274680 maxP=0 maxQ=0 maxS=0 fullRig=false'
for name in ('rig-input-godot-debug-final.log', 'rig-input-godot-optimize.log'):
    text = read(name)
    assert text.count(marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT code=0' in text
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
for name, marker in (
    ('rig-input-hierarchy-debug-regression.log', 'LYRA_RIG_HIERARCHY_GODOT_OK batches=37 '),
    ('rig-input-construction-debug-regression.log', 'LYRA_FOOTPLANT_RIG_CONSTRUCTION_GODOT_OK constructions=12 ')):
    text = read(name)
    assert text.count(marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT code=0' in text
    assert not re.search(r'^\s*(ERROR|WARNING):', text, re.M)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
optimized = read('rig-input-godot-optimize.log')
assert optimized.count('LYRA_OPTIMIZED_ASSEMBLY') == 3 and 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in optimized
assert 'LYRA_HIERARCHY_REGRESSION_PROCESS_EXIT code=0' in optimized
assert 'LYRA_CONSTRUCTION_REGRESSION_PROCESS_EXIT code=0' in optimized
for name in ('rig-input-debug-build-final.log', 'rig-input-optimize-build.log'):
    text = read(name)
    assert re.search(r'0\s*(个警告|Warning)', text) and re.search(r'0\s*(个错误|Error)', text)
    checks[name] = dict(sha256=sha(logs / name), warnings=0, errors=0)
comparison = json.loads((logs / 'rig-input-instrumentation-comparison.json').read_bytes())
assert comparison['changedOutputs'] == 47 and comparison['fullSolverOracleReplacement'] is False
assert all(v['input'] == v['alpha'] == v['boolBefore'] == v['boolUpdated'] == 0 for k, v in comparison.items() if '-' in k)
sources = ['src/Als.Godot/Animation/Lyra/LyraFootPlantRigHierarchy.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigInputTransfer.cs',
           'src/Als.Godot/Animation/Lyra/LyraFootPlantRigInputSmoke.cs',
           'tools/unreal/export_lyra_footplant_rig_inputs.py', 'scripts/export-lyra-footplant-rig-inputs.ps1']
report = dict(stage='OriginalMain73ALSInputTransfer', poseInputAccepted=True, sourceCurveClearingAccepted=True,
              mappedPositiveCurveTransferAccepted=False, outputTransferAccepted=False, fullRigPoseAccepted=False,
              production=False, counts=counts, mappedBones=69, unmappedTargetChannels=12, resetBones=22,
              hierarchySpaceFlags=26, nativeSha256=component['nativeSha256'],
              transformsComparedPerConfiguration=1058400, curveStatesComparedPerConfiguration=274680,
              maxPositionCm=0, maxQuaternion=0, maxScale=0,
              protectedPackages=len(native['assetSha256']), protectedFixtures=len(native['previousFixtureSha256']),
              instrumentationComparison=comparison, checks=checks, sources={p: sha(repo / p) for p in sources})
(logs / 'lyra-rig-input-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LYRA_RIG_INPUT_VERIFIED poseInput=true curveClearing=true positiveCurves=false outputTransfer=false fullRig=false production=false')

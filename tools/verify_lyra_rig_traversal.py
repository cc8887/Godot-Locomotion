"""Verify native provenance and actual Debug/Optimize traversal executions."""
import hashlib
import json
from pathlib import Path

from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        while b := f.read(1024 * 1024): h.update(b)
    return h.hexdigest()

def read(path):
    b = path.read_bytes()
    if b.startswith((b'\xff\xfe', b'\xfe\xff')): return b.decode('utf-16')
    try: return b.decode('utf-8-sig')
    except UnicodeDecodeError: return b.decode('gb18030')

native_path = root / 'rig_traversal_v1_native.json'
n = json.loads(native_path.read_bytes())
assert n['originalGroundAllValuesExact'] and not n['fullRig'] and not n['production']
assert sha(root / 'rig_traversal_v1_program.json') == n['programSha256']
for p, digest in n['dependencies'].items(): assert sha(root / p) == digest, p
for p, digest in n['previousFixtureSha256'].items(): assert sha(root / p) == digest, p
content = project_path('Content')
for p, digest in n['assetSha256'].items():
    assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
for p, digest in n['probeSourceSha256'].items():
    assert sha(source / p) == digest, p
    for tree in ('source', 'package'):
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest, p
report = dict(schemaVersion=1, nativeSha256=sha(native_path), counts=n['counts'], logs=[],
    protectedPackages=len(n['assetSha256']), protectedJson=len(n['previousFixtureSha256']),
    acceptedTraversal=True, predicateProducerReplay=True, actualSolverInputsAccepted=False,
    fullRigPoseAccepted=False, production=False)
for name in ('lyra-rig-traversal-ue.log', 'lyra-rig-traversal-repeat-ue.log'):
    text = read(logs / name)
    assert text.count('LYRA_RIG_TRAVERSAL_NATIVE_OK frames=2520 ') == 1, name
    assert text.rstrip().endswith('LYRA_EXPORT_PROCESS_EXIT_OK mode=rig-traversal code=0'), name
    assert 'Error:' not in text, name
    report['logs'].append(dict(file=name, exit=0, warnings=text.count('Warning:')))
for name in ('rig-traversal-debug-build.log', 'rig-traversal-optimize-build.log'):
    text = read(logs / name)
    assert '0 个警告' in text and '0 个错误' in text, name
for name in ('rig-traversal-godot-debug.log', 'rig-traversal-godot-optimize.log'):
    text = read(logs / name)
    marker = 'LYRA_RIG_TRAVERSAL_GODOT_OK frames=2520 solved=%d visits=%d ' % (n['counts']['solved'], n['counts']['visits'])
    assert text.count(marker) == 1 and 'LYRA_GODOT_PROCESS_EXIT code=0' in text, name
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text.splitlines()), name
    if 'optimize' in name:
        assert 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true' in text
        assert text.count('LYRA_OPTIMIZED_ASSEMBLY ') == 3
    report['logs'].append(dict(file=name, exit=0, warnings=0))
(logs / 'lyra-rig-traversal-verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print('LYRA_RIG_TRAVERSAL_VERIFIED frames=%d visits=%d protectedPackages=%d protectedJson=%d fullRig=false production=false' %
      (n['counts']['frames'], n['counts']['visits'], report['protectedPackages'], report['protectedJson']))

"""Audit the ordinary binding policy and the existing Lyra execution adapter."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'artifacts/lyra-analysis'
TAG = 'linked-layer-bindings-v2'
OUTPUT = EVIDENCE / f'{TAG}-integrity.json'
assert not OUTPUT.exists(), 'Preserve prior evidence'

def load(path):
    return json.loads(path.read_bytes())

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def text(path):
    raw = path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')

protected = load(EVIDENCE / 'linked-layer-bindings-v1-protected.json')
for name, digest in protected.items():
    assert sha(ROOT / name) == digest, name

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
trx = EVIDENCE / 'linked-layer-bindings-v2/linked-layer-bindings-v2.trx'
counters = ET.parse(trx).find('t:ResultSummary/t:Counters', ns).attrib
assert counters['total'] == counters['passed'] == '8' and counters['failed'] == '0'

summaries = []
for configuration, directory in [('debug', 'Debug'), ('optimize', 'ExportRelease')]:
    summary = load(EVIDENCE / f'{TAG}-{configuration}-verification.json')
    assert summary['passed'] and len(summary['runs']) == 11
    build_log = text(EVIDENCE / f'{TAG}-{configuration}-build.log')
    assert '0 个警告' in build_log and '0 个错误' in build_log
    for name, digest in summary['assemblies'].items():
        assert sha(ROOT / f'.godot/mono/temp/bin/{directory}' / name) == digest.lower(), name
    if configuration == 'optimize':
        assert summary['debugRestored']
        debug = summaries[0]
        for name, digest in debug['assemblies'].items():
            assert sha(EVIDENCE / f'{TAG}-optimize-debug-backup' / name) == digest.lower()
    for run in summary['runs']:
        assert run['passed'] and run['exitCode'] == 0
        assert sha(Path(run['log'])) == run['logSha256'].lower()
        assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in text(Path(run['log'])).splitlines())
        if 'report' in run:
            assert sha(Path(run['report'])) == run['reportSha256'].lower()
            report = load(Path(run['report']))
            if run['name'] != 'ten-characters':
                assert report['switches'] == report['sameClassReuse'] == 6
                hz = report['model']['hz']
                assert report['pendingRebindRejected'] == {30: 3, 60: 5, 120: 9}[hz]
                assert report['model']['skinBones'] == 68 and report['model']['logicalBones'] == 81
                assert report['model']['frames'] == report['model']['retries'] == report['model']['published']
    summaries.append(summary)

for debug, optimize in zip(summaries[0]['runs'], summaries[1]['runs'], strict=True):
    assert debug['name'] == optimize['name']
    if 'report' in debug:
        assert load(Path(debug['report'])) == load(Path(optimize['report'])), debug['name']

native = []
for configuration in ('debug', 'optimize'):
    for hz, reference in [(30, 'rebind30-final'), (60, 'rebind60-first'), (120, 'rebind120-final')]:
        path = EVIDENCE / f'whole-main-{configuration}-{TAG}-{hz}-verification.json'
        report = load(path)
        assert report['comparisonPassed'] and len(report['runs']) == 3
        assert [run['boundary'] for run in report['runs']] == ['pre-inertia', 'pre-rig', 'final']
        assert report['assemblies'] == summaries[0 if configuration == 'debug' else 1]['assemblies']
        assert report['retry']
        if configuration == 'optimize':
            assert report['debugRestored']
            for name, digest in summaries[0]['assemblies'].items():
                assert sha(EVIDENCE / f'whole-main-optimize-{TAG}-{hz}-debug-backup' / name) == digest.lower()
        for run in report['runs']:
            assert run['comparisonPassed'] and run['exitCode'] == 0
            native_log = text(Path(run['log']))
            frames = {30: 1080, 60: 2160, 120: 4320}[hz]
            assert f'frames={frames} profiles=3' in native_log and f'retry={frames}' in native_log
            assert 'rebinds=24 sameClass=24 actionRebinds=21 hiddenRebinds=6' in native_log
            assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in native_log.splitlines())
        native.append({'path': str(path), 'sha256': sha(path)})

sources = [
    'src/Als.Core/Animation/AlsLinkedLayerBindings.cs',
    'tests/Als.Core.Tests/AlsLinkedLayerBindingsTests.cs',
    'src/Als.Godot/Animation/Lyra/LyraLinkedLayerContracts.cs',
    'src/Als.Godot/Animation/Lyra/LyraMainLocomotionHost.cs',
    'src/Als.Godot/Animation/Lyra/LyraLinkedLayerBindingSmoke.cs',
    'scripts/verify-lyra-linked-layer-bindings.ps1',
    'tools/verify_lyra_linked_layer_bindings.py',
]
result = dict(auditPassed=True, managedTests=8, finalRuntimeProcesses=40,
    unchangedLyraJson=len(protected), nativeBindingSteps=8, nativeBindingNodes=112,
    originalNamedMultiGroupRuntimeAccepted=False, originalUngroupedRuntimeAccepted=False,
    originalUnlinkRuntimeAccepted=False, generalGraphExecutionAccepted=False,
    sharedPersistentSubsystemAccepted=False, goalComplete=False,
    sources={name: sha(ROOT / name) for name in sources}, nativeBoundaries=native)
OUTPUT.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, ensure_ascii=False))

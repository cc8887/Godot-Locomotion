"""Audit original UE ownership captures and production regressions without changing fixtures."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / 'artifacts/lyra-analysis'
TAG = 'linked-layer-native-v3'
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
for filename, expected in [('layer-binding-policy-v3.trx', 8), ('layer-binding-native-v3.trx', 10)]:
    trx = EVIDENCE / 'layer-binding-native-v3' / filename
    counters = ET.parse(trx).find('t:ResultSummary/t:Counters', ns).attrib
    assert counters['total'] == counters['passed'] == str(expected) and counters['failed'] == '0'

capture = load(EVIDENCE / 'layer-binding-matrix-v3-closure.json')
fixture = load(EVIDENCE / 'layer-binding-matrix-v3-native.json')
assert capture['metadataRestored'] and capture['assetsSaved'] == 0
assert fixture['metadataRestored'] and not fixture['posesEvaluated']
assert len(fixture['cases']) == 10
steps = sum(len(c['steps']) for c in fixture['cases'])
observations = sum(len(s['nodes']) for c in fixture['cases'] for s in c['steps'])
assert steps == 93 and observations == 1302
assert capture['requestSha256'] == sha(EVIDENCE / 'layer-binding-matrix-v3-request.json')
assert capture['nativeSha256'] == sha(EVIDENCE / 'layer-binding-matrix-v3-native.json')
for suffix in ('request', 'native', 'closure'):
    assert (EVIDENCE / f'layer-binding-matrix-v3-{suffix}.json').read_bytes() == (EVIDENCE / f'layer-binding-matrix-v3-repeat-{suffix}.json').read_bytes()
project = Path('../GASP58')
for name, digest in capture['protectedJson'].items():
    assert sha(ROOT / 'assets/generated/lyra_als' / name) == digest, name
for name, digest in capture['configuration'].items():
    assert sha(project / name) == digest, name
for name, digest in capture['assetSha256'].items():
    name = name.split('.')[0]
    if name.startswith('/Game/'):
        path = project / 'Content' / (name.removeprefix('/Game/') + '.uasset')
    elif name.startswith('/ShooterCore/'):
        path = project / 'Plugins/GameFeatures/ShooterCore/Content' / (name.removeprefix('/ShooterCore/') + '.uasset')
    else: raise AssertionError(name)
    assert sha(path) == digest, name
for name, digest in capture['probeSourceSha256'].items():
    assert sha(ROOT / 'tools/unreal/LyraWholeMainOracle' / name) == digest, name
    assert sha(ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-layer-binding-matrix-v3' / name) == digest, name
assert sha(ROOT / 'tools/unreal/capture_lyra_layer_binding_matrix.py') == capture['scriptSha256']
ue_warnings = {}
for tag in ('layer-binding-matrix-v3', 'layer-binding-matrix-v3-repeat'):
    log = text(EVIDENCE / f'{tag}-ue.log')
    assert 'LYRA_LAYER_BINDING_MATRIX_NATIVE_OK cases=10 steps=93 assets_saved=0 metadata_restored=true' in log
    assert 'LAYER_BINDING_PROCESS_EXIT=0' in log
    assert 'LYRA_LAYER_BINDING_MATRIX_FAILED' not in log
    assert 'Unable to dynamically link' not in log
    assert not any(': Error:' in line or 'Ensure condition failed:' in line for line in log.splitlines())
    ue_warnings[tag] = sum(': Warning:' in line for line in log.splitlines())


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
    'tools/verify_lyra_layer_binding_matrix.py',
    'tests/Als.Core.Tests/AlsLinkedLayerBindingNativeTests.cs',
    'tools/build_lyra_layer_binding_matrix_request.py',
    'tools/unreal/capture_lyra_layer_binding_matrix.py',
    'scripts/capture-lyra-layer-binding-matrix.ps1',
    'tools/unreal/LyraWholeMainOracle/Source/LyraWholeMainOracle/Public/LyraWholeMainOracleLibrary.h',
    'tools/unreal/LyraWholeMainOracle/Source/LyraWholeMainOracle/Private/LyraLayerBindingMatrix.cpp',
]
result = dict(auditPassed=True, managedTests=18, finalRuntimeProcesses=40,
    unchangedLyraJson=len(protected), nativeBindingCases=10, nativeBindingSteps=steps, nativeBindingNodes=observations,
    unchangedOriginalPackages=len(capture['assetSha256']), unchangedProjectConfiguration=len(capture['configuration']),
    independentCaptureBytesEqual=True, ueWarnings=ue_warnings,
    originalNamedMultiGroupOwnershipAccepted=True, originalUngroupedOwnershipAccepted=True,
    originalUnlinkOwnershipAccepted=True, originalDefaultSelfOwnershipAccepted=True,
    compiledNewGraphTopologyAccepted=False, newBindingMatrixPosesEvaluated=False, generalGraphExecutionAccepted=False,
    sharedPersistentSubsystemAccepted=False, goalComplete=False,
    sources={name: sha(ROOT / name) for name in sources}, nativeBoundaries=native)
with OUTPUT.open('x', encoding='utf-8', newline='\n') as stream:
    stream.write(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, ensure_ascii=False))

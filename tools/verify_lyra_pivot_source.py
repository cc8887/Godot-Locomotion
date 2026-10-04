"""Verify protected resources and the recorded native/runtime component gates."""
import hashlib
import json
import re
from pathlib import Path
from locomotion_paths import project_path

import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
root = repo/'assets/generated/lyra_als'
artifacts = repo/'artifacts/lyra-analysis'
content = project_path('Content')
sha = lambda data: hashlib.sha256(data).hexdigest()
read = lambda name: json.loads((root/name).read_bytes())
native, requests, definitions = map(read, ('pivot_source_native.json', 'pivot_source_requests.json', 'pivot_source_definitions.json'))
request_sha = sha((root/'pivot_source_requests.json').read_bytes())
assert native['requestSha256'] == definitions['requestSha256'] == request_sha
for name, expected in native['dependencies'].items():
    assert sha((root/name).read_bytes()) == expected, name
for name, expected in native['previousFixtureSha256'].items():
    assert sha((root/name).read_bytes()) == expected, name
for path, expected in native['assetSha256'].items():
    package = content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')
    assert sha(package.read_bytes()) == expected, path
assert len(native['assetSha256']) == 508 and len(native['previousFixtureSha256']) == 605
assert len(native['traces']) == len(requests['traces']) == 9
assert sum(len(trace['frames']) for trace in native['traces']) == 3780
assert len(definitions['assets']) == 36

logs = {}
for name, marker in (
        ('pivot-source-godot-final.log', 'LYRA_PIVOT_SOURCE_GODOT_OK'),
        ('pivot-source-regression-start.log', 'LYRA_START_SOURCE_GODOT_OK'),
        ('pivot-source-regression-stop.log', 'LYRA_STOP_SOURCE_GODOT_OK'),
        ('pivot-source-regression-main-history.log', 'LYRA_MAIN_STATE_HISTORY_JOINT_OK')):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    successes = [line for line in text.splitlines() if marker in line]
    assert len(successes) == 1 and not re.search(r'(?m)^(ERROR|WARNING):', text), name
    logs[name] = {'sha256': sha((artifacts/name).read_bytes()), 'success': successes[0]}
for name in ('pivot-source-ue-first-pass.log', 'pivot-source-ue-export.log'):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert text.count('LYRA_PIVOT_SOURCE_NATIVE_OK traces=9 frames=3780 packages=508 assets=36 assets_saved=0') == 1, name
    errors = re.findall(r'(?m)^.*(?:Error:|Fatal error|Assertion failed|Ensure condition failed)', text)
    assert not errors, (name, errors[:3])
    logs[name] = {'sha256': sha((artifacts/name).read_bytes()), 'errors': 0,
                  'warningLinesIncludingSummary': len(re.findall(r'(?m)^.*Warning:', text))}
for name in ('pivot-source-build-debug-transaction.log', 'pivot-source-build-optimize.log'):
    text = (artifacts/name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text, name
    logs[name] = {'sha256': sha((artifacts/name).read_bytes()), 'errors': 0, 'warnings': 0}
counters = ET.parse(artifacts/'pivot-source-core.trx').find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert counters['total'] == counters['passed'] == '56' and counters['failed'] == '0'
cpp = Path('Source/AlsV4AssetExporter/Private/AlsLyraCycleLibrary.cpp')
header = Path('Source/AlsV4AssetExporter/Public/AlsLyraGraphLibrary.h')
for source in (cpp, header):
    expected = sha((repo/'tools/unreal/AlsV4AssetExporter'/source).read_bytes())
    assert sha((repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'/source).read_bytes()) == expected
    assert sha((repo/'artifacts/unreal/gasp58-lyra-masks/source/AlsV4AssetExporter'/source).read_bytes()) == expected
report = {'schemaVersion': 1, 'protectedPackages': 508, 'protectedPreviousFixtures': 605,
          'frames': 3780, 'distanceAssets': 36, 'coreTests': counters, 'logs': logs,
          'newResourceSha256': {name: sha((root/name).read_bytes()) for name in (
              'pivot_source_requests.json', 'pivot_source_definitions.json', 'pivot_source_native.json')},
          'scope': 'Original dual evaluator callbacks, shared fields, transition delegate, prediction and common Sync under explicit visits/Main observations.',
          'wholePivotMachine': False, 'providerPose': False, 'ordinaryDemo': False, 'wholeGoalComplete': False}
(artifacts/'pivot-source-final-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('LYRA_PIVOT_SOURCE_FINAL_VERIFIED frames=3780 packages=508 fixtures=605 core=56 whole_goal=false')

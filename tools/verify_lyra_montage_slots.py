"""Verify current Montage metadata, native captures, runtime logs and builds."""
import hashlib
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
logs = repo / 'artifacts/lyra-analysis'
content = Path('../GASP58/Content')
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads((root / p).read_bytes())
catalog = read('montage_catalog_v2.json')
assert len(catalog['assets']) == 45 and len(catalog['sequences']) == 55
assert sum(len(a['slots']) == 2 for a in catalog['assets']) == 15
assert sum(len(t['segments']) for a in catalog['assets'] for t in a['slots']) == 60
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
protected = {}
for document in (catalog, read('montage_slots_v1_native.json'), read('montage_slots_v2_native.json')):
    for p, digest in document['previousFixtureSha256'].items():
        assert sha(root / p) == digest, 'Changed previous fixture: ' + p
        protected[p] = digest
    for p, digest in document['assetSha256'].items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, p
    for p, digest in document['dependencies'].items():
        assert sha(root / p) == digest, p
for document in (catalog, read('montage_slots_v2_native.json')):
    for p, digest in document['probeSourceSha256'].items():
        assert sha(source / p) == digest, p
        for tree in ('source', 'package'):
            assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == digest, (tree, p)
for version in ('v1', 'v2'):
    native = read('montage_slots_' + version + '_native.json')
    request_name = 'montage_slots_' + version + '_requests.json'
    assert sha(root / request_name) == native['requestSha256']
    requests = read(request_name)
    assert [t['hz'] for t in native['traces']] == [30, 60, 120]
    assert native['counts']['frames'] == 13440 and native['counts']['slots'] == 67200
    assert all(native['counts'][p] > 0 for p in ('updated', 'inactive', 'hidden', 'full', 'overlap', 'instances'))
    played = {c['asset'] for t in requests['traces'] for f in t['frames'] for c in f['commands'] if not c['stop']}
    assert played == set(range(45))
    modes = {c.get('stopGroup', False) for t in requests['traces'] for f in t['frames'] for c in f['commands'] if not c['stop']}
    assert modes == ({False} if version == 'v1' else {False, True})

def clean_log(name, marker):
    text = (logs / name).read_text(encoding='utf-8-sig')
    assert len(re.findall(marker, text)) == 1, name
    assert not re.search(r'(?m)^ERROR:|LogPython: Error:|LogWindows: Error:|Assertion failed|Ensure condition failed', text), name
    return text

ue_warnings = {}
for name, mode, marker in (
    ('lyra-montage-catalog-v2-ue.log', 'montage-catalog', 'LYRA_MONTAGE_CATALOG_OK montages=45 sequences=55 slots=5'),
    ('lyra-montage-catalog-v2-ue-repeat.log', 'montage-catalog', 'LYRA_MONTAGE_CATALOG_OK montages=45 sequences=55 slots=5'),
    ('lyra-montage-slots-v2-ue.log', 'montage-slots', 'LYRA_MONTAGE_SLOTS_NATIVE_OK frames=13440 slots=67200 assets=45'),
    ('lyra-montage-slots-v2-ue-repeat.log', 'montage-slots', 'LYRA_MONTAGE_SLOTS_NATIVE_OK frames=13440 slots=67200 assets=45')):
    text = clean_log(name, marker)
    assert 'LYRA_EXPORT_PROCESS_EXIT_OK mode=' + mode + ' code=0' in text
    ue_warnings[name] = len(re.findall(r'Warning:', text))
for version, name in (('v1', 'lyra-montage-slots-v1-godot-final.log'), ('v2', 'lyra-montage-slots-v2-godot.log')):
    text = clean_log(name, 'LYRA_MONTAGE_SLOTS_GODOT_OK fixture=montage_slots_' + version)
    assert 'clocks=exact weights=exact contexts=exact' in text and 'code=0' in text
    assert 'WARNING:' not in text
text = clean_log('lyra-montage-main-pose-regression.log', 'LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK frames=11340 poses=9762')
assert 'actualFinalFeedback=True' in text and 'code=0' in text and 'WARNING:' not in text
for name in ('lyra-montage-v2-debug-final.log', 'lyra-montage-optimize-final.log'):
    text = (logs / name).read_text(encoding='utf-8-sig')
    assert '0 个警告' in text and '0 个错误' in text and '已成功生成' in text, name
build = (logs / 'lyra-montage-v2-ue-build.log').read_text(encoding='utf-8-sig')
assert 'BUILD SUCCESSFUL' in build and 'AutomationTool exiting with ExitCode=0' in build
trx = ET.parse(logs / 'lyra-montage-core-final.trx')
counter = trx.find('.//{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}Counters').attrib
assert counter['total'] == counter['passed'] == '194' and counter['failed'] == '0'
report = dict(assets=45, sequences=55, dualTrackAssets=15, tracks=60, nativeFrames=26880,
              slotRows=134400, originalPackagePaths=len(catalog['assetSha256']), protectedPreviousJson=len(protected),
              coreTests=194, ueWarnings=ue_warnings, mainRegressionFrames=11340,
              scope='Montage metadata, physical clocks, fades, multitrack snapshots and original five Slot source-update contexts. Pose/Notify/RootMotion extraction and production integration remain open.')
(logs / 'lyra-montage-slots-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('LYRA_MONTAGE_SLOTS_VERIFICATION_OK ' + json.dumps(report, separators=(',', ':')))

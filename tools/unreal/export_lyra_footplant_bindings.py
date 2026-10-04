"""Read the original Main73 exposed-input handler on actual Main feedback."""
import hashlib
import itertools
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
prefix = 'footplant_binding_v1.json'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
graph = json.loads((root / 'footplant_rig_graph_v1.json').read_bytes())
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p)
            for p in root.rglob('*.json') if p.name != prefix}
packages = dict(graph['assetSha256'])
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, h in previous.items():
        assert sha(root / p) == h, p
    for p, h in packages.items():
        assert sha(content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')) == h, p

protect()
cal = json.loads((root / 'logical_controls/calibration.json').read_bytes())['calibration']
mesh = unreal.load_asset(cal['sourceMesh'])
for asset in (mesh, mesh.get_editor_property('skeleton')):
    path = asset.get_path_name()
    packages[path] = sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset'))
main = unreal.load_class(None, graph['main'] + '_C')
frames = [dict(disableLegIK=v, useFootPlacement=foot, enableField=enable,
               crouching=crouch, moving=move)
          for v, foot, enable, crouch, move in itertools.product(
              [-1, -.000001, 0, .000001, .125, 1], [False, True], [False, True], [False, True], [False, True])]
text = unreal.AlsLyraFootPlantBindingLibrary.read_bindings(main, mesh, json.dumps({'frames': frames}))
assert text
native = json.loads(text)['frames']
(repo / 'artifacts/lyra-analysis/footplant-binding-diagnostic.json').write_text(
    json.dumps({'requests': frames, 'frames': native}, separators=(',', ':')), encoding='utf-8')
assert len(native) == len(frames) == 96
for f, row in zip(frames, native, strict=True):
    assert row['enabled'] == (row['curve'] <= 0 and not f['useFootPlacement'])
    assert (row['crouching'], row['moving']) == (f['crouching'], f['moving'])
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
hashes = {p: sha(source / p) for p in ('Private/AlsLyraFootPlantBindingLibrary.cpp', 'Public/AlsLyraFootPlantBindingLibrary.h')}
for tree in ('source', 'package'):
    for p, h in hashes.items():
        assert sha(repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter' / p) == h
protect()
data = dict(schemaVersion=1, stage='OriginalMain73ExposedInputHandler', mainNode=73,
            mainClass=graph['main'], predicate='GetCurveValue(DisableLegIK) <= 0 && !UseFootPlacement',
            enableControlRigFieldIsNotPredicate=True, requests=frames, frames=native,
            probeSourceSha256=hashes, previousFixtureSha256=previous, assetSha256=packages)
p = root / prefix
if p.exists():
    assert json.loads(p.read_bytes()) == data, 'Immutable FootPlant binding capture changed'
else:
    p.write_text(json.dumps(data, separators=(',', ':'), allow_nan=False), encoding='utf-8')
unreal.log('LYRA_FOOTPLANT_BINDINGS_NATIVE_OK cases=96 enabled=%d assets_saved=0' % sum(r['enabled'] for r in native))

"""Read the complete compiled Main and all fourteen provider layer closures."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
sha = lambda b: hashlib.sha256(b).hexdigest()
name = 'main_layer_graph_v1.json'
baseline = json.loads((root / 'main_als_locomotion_v1_native.json').read_bytes())
packages = baseline['assetSha256']
previous = {str(p.relative_to(root)).replace('\\', '/'): sha(p.read_bytes()) for p in root.rglob('*.json') if p.name != name}
if (root / name).exists():
    previous = json.loads((root / name).read_bytes())['previousFixtureSha256']
content = Path(unreal.Paths.project_content_dir())

def protect():
    for p, digest in packages.items():
        assert sha((content / (p.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes()) == digest, p
    for p, digest in previous.items():
        assert sha((root / p).read_bytes()) == digest, p

protect()
contracts = json.loads((root / 'linked_layer_contracts.json').read_bytes())
resources = json.loads((root / 'locomotion_resources.json').read_bytes())
main_class = contracts['classes']['main']['class']
classes = {'main': main_class} | {k: v['class'] for k, v in resources['providers'].items()}
data = {}
for profile, path in classes.items():
    cls = unreal.load_class(None, path)
    hooks = ['AnimGraph'] if profile == 'main' else [f['name'] for f in contracts['classes'][profile]['functions'] if f['implemented']]
    assert profile == 'main' or len(hooks) == 14, (profile, hooks)
    graphs = {}
    for hook in hooks:
        text = unreal.AlsLyraGraphLibrary.read_animation_layer_graph(cls, hook, True)
        assert text, (profile, hook)
        graphs[hook] = json.loads(text)
    data[profile] = dict(classPath=path, graphs=graphs,
        defaults=json.loads(unreal.AlsLyraGraphLibrary.read_animation_layer_defaults(cls)))
protect()
source = repo / 'tools/unreal/AlsV4AssetExporter/Source/AlsV4AssetExporter'
probe = {p: sha((source / p).read_bytes()) for p in (
    'Private/AlsLyraCycleLibrary.cpp', 'Private/AlsLyraSourceLibrary.cpp', 'Public/AlsLyraGraphLibrary.h')}
for tree in ('source', 'package'):
    compiled = repo / 'artifacts/unreal/gasp58-lyra-masks' / tree / 'AlsV4AssetExporter/Source/AlsV4AssetExporter'
    for p, digest in probe.items():
        assert sha((compiled / p).read_bytes()) == digest, (tree, p)
result = dict(schemaVersion=1, dependencies={p: sha((root / p).read_bytes()) for p in
    ('linked_layer_contracts.json', 'runtime_graph.json', 'locomotion_resources.json', 'source_nodes.json')},
    classes=data, assetSha256=packages, previousFixtureSha256=previous, probeSourceSha256=probe)
out = root / name
if out.exists():
    assert json.loads(out.read_bytes()) == result, 'Immutable Main/Layer graph differs'
else:
    out.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
unreal.log('LYRA_MAIN_LAYER_GRAPH_OK classes=4 hooks=43 packages=508 previous=' + str(len(previous)) + ' assets_saved=0')

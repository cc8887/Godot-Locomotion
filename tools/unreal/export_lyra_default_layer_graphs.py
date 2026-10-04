"""Export the actual compiled Main defaults, including unimplemented functions."""
import hashlib
import json
import os
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = Path(os.environ['LYRA_OUTPUT_ROOT'])
tag = os.environ['LYRA_DEFAULT_GRAPH_TAG']
assert root == repo/'assets/generated/lyra_als' and tag.replace('-', '').isalnum()
name = 'default_layer_graphs_v1.json'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
out = repo/'artifacts/lyra-analysis'
baseline = read(out/'layer-fallback-v4-closure.json')
previous = baseline['previousFixtureSha256']
project = Path(unreal.Paths.get_project_file_path()).parent
package = repo/'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter'
probe = {p.relative_to(package).as_posix(): sha(p) for p in package.rglob('*')
    if p.is_file() and (p.suffix in ('.h', '.cpp', '.cs', '.dll', '.modules', '.uplugin'))}

def original_file(path):
    path = path.split('.')[0]
    if path.startswith('/Game/'): return project/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'): return project/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)

def protect():
    current = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.json') if p.name != name}
    assert current == previous, 'Original generated inventory changed'
    for path, digest in baseline['assetSha256'].items(): assert sha(original_file(path)) == digest, path
    for path, digest in baseline['protectedProject'].items(): assert sha(project/path) == digest, path
    for path, digest in probe.items(): assert sha(package/path) == digest, path

protect()
contracts = read(root/'linked_layer_contracts.json')
main = contracts['classes']['main']
cls = unreal.load_class(None, main['class'])
reference = {f['name']: f for f in read(out/'layer-fallback-v4-native.json')['functions']}
main_graph = read(root/'main_layer_graph_v1.json')['classes']['main']['graphs']['AnimGraph']
property_count = main_graph['root'] + main_graph['rootPropertyIndex'] + 1
functions = {}
for function in main['functions']:
    hook = function['name']
    if hook == 'AnimGraph': continue
    assert not function['implemented']
    text = unreal.AlsLyraGraphLibrary.read_animation_layer_graph(cls, hook, True)
    assert text, hook
    graph = json.loads(text)
    native = reference[hook]
    # FAnimBlueprintFunction stores property order. The existing graph exporter
    # uses reversed compiled node indices; keep the two address spaces explicit.
    assert graph['rootPropertyIndex'] == native['rootIndex'] == native['rootPropertyIndex'], hook
    assert graph['root'] + graph['rootPropertyIndex'] + 1 == property_count, hook
    assert len(graph['nodes']) == len(native['nodes']) == 1 and graph['nodes'][0]['links'] == [], hook
    assert graph['nodes'][0]['type'] == '/Script/Engine.AnimNode_Root', hook
    functions[hook] = dict(signature=function, graph=graph)
assert len(functions) == 14
protect()
resource = dict(schemaVersion=1, classPath=main['class'], nodePropertyCount=property_count,
    dependencies={p: sha(root/p) for p in ('linked_layer_contracts.json','main_layer_graph_v1.json')},
    nativeReferenceSha256=sha(out/'layer-fallback-v4-native.json'), functions=functions,
    previousFixtureSha256=previous, assetSha256=baseline['assetSha256'],
    protectedProject=baseline['protectedProject'], probePackageSha256=probe,
    scope=dict(compiledDefaultRoots=True, assetsSaved=0))
content = json.dumps(resource, separators=(',', ':'), allow_nan=False).encode('utf-8')
path = root/name
if path.exists(): assert path.read_bytes() == content, 'Immutable default graph resource differs'
else:
    with path.open('xb') as f: f.write(content)
protect()
with (out/f'{tag}-closure.json').open('x', encoding='utf-8') as f:
    f.write(json.dumps(dict(schemaVersion=1, resourceSha256=sha(path), previousFixtureSha256=previous,
        assetSha256=baseline['assetSha256'], protectedProject=baseline['protectedProject'],
        exporterSha256=sha(Path(__file__)), probePackageSha256=probe,
        scope=dict(originalDefaultRoots=14, assetsSaved=0, productionUnlinkIntegrated=False, goalComplete=False)),separators=(',', ':'))+'\n')
unreal.log('LYRA_DEFAULT_LAYER_GRAPHS_OK roots=14 original_json=869 assets_saved=0')

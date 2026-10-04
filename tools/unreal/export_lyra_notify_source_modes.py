"""Read fixed Main and dynamically selected AimOffset notification policies."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
load = lambda name: json.loads((root / name).read_bytes())
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
dependencies = {name: sha(root / name) for name in ('notify_contract_v1.json',
    'aiming_layer_v1_policy.json', 'main_lean/inventory.json', 'main_layer_graph_v1.json')}
contract = load('notify_contract_v1.json')
spaces = {r['path'] for r in contract['blendSpaces']}
spaces.update(s['path'] for s in load('aiming_layer_v1_policy.json')['spaces'])
rows = []
for path in sorted(spaces):
    asset = unreal.load_asset(path)
    assert isinstance(asset, unreal.BlendSpace), path
    row = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(asset))
    assert row['source'] == path and row['notifyMode'] in (0, 1, 2)
    assert all(s['sequence'] in {a['source'] for a in contract['assets']} for s in row['samples'])
    rows.append(row)
assert len(rows) == 4
content = Path(unreal.Paths.project_content_dir())
for path, digest in contract['assetSha256'].items():
    assert sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')) == digest, path
for name, digest in dependencies.items():
    assert sha(root / name) == digest, name
result = dict(schemaVersion=1, dependencies=dependencies, spaces=rows, assetsSaved=0)
output = root / 'notify_source_modes_v1.json'
if output.exists():
    assert load(output.name) == result, 'Immutable notify source policy differs'
else:
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(result, separators=(',', ':'), allow_nan=False) + '\n')
unreal.log('LYRA_NOTIFY_SOURCE_MODES_OK spaces=4 assets_saved=0')

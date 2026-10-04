"""Audit the read-only RigVM capture and report explicit ALS bone bindings."""
import collections
import hashlib
import json
from pathlib import Path


from locomotion_paths import project_path

repo = Path(__file__).resolve().parents[1]
root = repo / 'assets/generated/lyra_als'
path = root / 'footplant_rig_graph_v1.json'
data = json.loads(path.read_bytes())
calibration_path = root / 'logical_controls/calibration.json'
calibration = json.loads(calibration_path.read_bytes())
names = {name.casefold(): name for name in calibration['layout']['logicalBoneNames']}
assert len(names) == 81
references = collections.defaultdict(list)


def walk_pins(pins):
    for pin in pins:
        yield pin
        yield from walk_pins(pin['subPins'])


for graph in data['graphs']:
    pins = {p['path']: p for n in graph['nodes'] for p in walk_pins(n['pins'])}
    for link in graph['links']:
        assert link['source'] in pins and link['target'] in pins, link
    for node in graph['nodes']:
        for pin in walk_pins(node['pins']):
            value = pin['default']
            if pin['type'] == 'FRigElementKey' and 'Type=Bone,Name="' in value:
                name = value.split('Name="', 1)[1].split('"', 1)[0]
                if name != 'None':
                    references[name].append({'graph': graph['objectPath'], 'node': node['name'],
                                              'pin': pin['path']})

sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
for name, digest in data['previousFixtureSha256'].items():
    assert sha(root / name) == digest, name
content = project_path('Content')
for name, digest in data['assetSha256'].items():
    asset = content / (name.split('.')[0].removeprefix('/Game/') + '.uasset')
    assert sha(asset) == digest, name
logs = []
for name in ('lyra-footplant-rig-graph-ue.log', 'lyra-footplant-rig-graph-repeat-ue.log'):
    lines = (repo / 'artifacts/lyra-analysis' / name).read_text(encoding='utf-8-sig').splitlines()
    markers = [l for l in lines if 'LYRA_FOOTPLANT_RIG_GRAPH_OK' in l]
    assert len(markers) == 1 and 'assets_saved=0' in markers[0], name
    assert lines[-1] == 'LYRA_EXPORT_PROCESS_EXIT_OK mode=footplant-rig-graph code=0', name
    assert not any('Error:' in line for line in lines), name
    logs.append({'file': name, 'exit': 0, 'warnings': sum('Warning:' in l for l in lines)})

report = {'schemaVersion': 1, 'captureSha256': sha(path),
          'calibrationSha256': sha(calibration_path),
          'graphs': len(data['graphs']), 'nodes': sum(len(g['nodes']) for g in data['graphs']),
          'links': sum(len(g['links']) for g in data['graphs']),
          'hierarchy': dict(collections.Counter(e['type'] for e in data['hierarchy'])),
          'mainDefaults': data['mainDefaults'], 'variables': len(data['variables']),
          'protectedPackages': len(data['assetSha256']),
          'protectedJson': len(data['previousFixtureSha256']),
          'boneBindings': {name: {'target': names.get(name.casefold()), 'references': rows}
                           for name, rows in sorted(references.items())},
          'logs': logs,
          'scope': 'Structural read-only capture and target-name audit; no Rig execution or Godot validation.'}
output = repo / 'artifacts/lyra-analysis/footplant-rig-graph-analysis.json'
output.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n', encoding='utf-8')
print('LYRA_FOOTPLANT_RIG_GRAPH_AUDITED graphs=%d nodes=%d packages=%d json=%d unmapped=%s' % (
    report['graphs'], report['nodes'], report['protectedPackages'], report['protectedJson'],
    ','.join(name for name, row in report['boneBindings'].items() if row['target'] is None)))

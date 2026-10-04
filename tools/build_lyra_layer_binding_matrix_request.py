"""Controlled layouts for original UE binding APIs; no pose expectations."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
contracts = json.loads((ROOT / 'assets/generated/lyra_als/linked_layer_contracts.json').read_bytes())['classes']
nodes = contracts['main']['linkedNodes']
names = [node['layer'] for node in nodes]
providers = ['unarmed', 'pistol', 'rifle']
classes = {name: contracts[name]['class'] for name in ['main', *providers]}
flags = {node['node']: {'receiveNotifies': index % 2 == 0, 'propagateNotifies': index % 3 == 0}
         for index, node in enumerate(nodes)}
def operations(*items):
    return [{'kind': kind, 'class': name} for kind, name in items]
def layout(name, groups, ops, node_overrides=None, class_flags=None, extra_functions=None):
    functions = {provider: {function: {'group': groups[index]} for index, function in enumerate(names)}
                 for provider in providers}
    for provider, overrides in (extra_functions or {}).items():
        for function, values in overrides.items():
            functions.setdefault(provider, {}).setdefault(function, {}).update(values)
    overrides = {node: dict(values) for node, values in flags.items()}
    for node, values in (node_overrides or {}).items():
        overrides[node].update(values)
    return dict(name=name, functions=functions, nodes=overrides, classFlags=class_flags or {}, operations=ops)

common = operations(('link', 'unarmed'), ('link', 'unarmed'), ('link', 'pistol'), ('link', 'pistol'),
                    ('unlink', 'unarmed'), ('unlink', 'pistol'), ('link', 'rifle'), ('link', 'rifle'), ('link', ''))
cases = [
    layout('named-three-groups', [['Body', 'Aim', 'Hands'][index % 3] for index in range(14)], common,
           class_flags={'main': {'receiveNotifies': True, 'propagateNotifies': False},
                        'rifle': {'receiveNotifies': False, 'propagateNotifies': True}}),
    layout('mixed-named-and-none', [['Body', '', 'Hands'][index % 3] for index in range(14)], common),
]
duplicate = {node['node']: {'function': 'FullBody_CycleState'} for node in nodes
             if not node['inputPoses'] and node['layer'] in ['FullBody_FallLoopState', 'FullBody_FallLandState']}
cases.append(layout('ungrouped-duplicate-function', [''] * 14, common, duplicate,
                    {provider: {'receiveNotifies': True, 'propagateNotifies': True} for provider in providers}))
cases.append(layout('partial-first-node-keeps-class', ['Body'] * 14,
                    operations(('link', 'unarmed'), ('link', 'pistol'), ('link', 'unarmed'), ('link', 'pistol'),
                               ('unlink', 'pistol'), ('link', 'unarmed'), ('link', 'rifle'), ('unlink', 'rifle')),
                    extra_functions={'pistol': {names[0]: {'implemented': False}}}))
for mode in ['all-grouped', 'all-none', 'mixed-classes', 'mixed-default-and-self']:
    defaults = {node['node']: {'defaultClass':
                ('pistol' if mode == 'mixed-classes' and index % 2 else
                 '' if mode == 'mixed-default-and-self' and index % 2 else 'unarmed')}
                for index, node in enumerate(nodes)}
    reuse = operations(('link', 'unarmed'), ('link', 'unarmed')) if mode == 'mixed-classes' else []
    cases.append(layout('defaults-' + mode, ['' if mode == 'all-none' else 'Body'] * 14,
                        reuse + operations(('link', ''), ('link', 'rifle'), ('link', 'rifle'), ('unlink', 'pistol'),
                                   ('unlink', 'rifle'), ('unlink', ''), ('link', 'pistol'), ('link', '')),
                        defaults))
zero = next(node for node in nodes if not node['inputPoses'])
cases.append(layout('no-interface-with-default', ['Body'] * 14, common,
                    {zero['node']: {'hasInterface': False, 'defaultClass': 'unarmed'}}))
cases.append(layout('main-selector-default-recreation', ['Body'] * 14,
                    operations(('link', 'unarmed'), ('link', 'main'), ('link', 'main'), ('link', ''), ('unlink', 'unarmed')),
                    {zero['node']: {'defaultClass': 'unarmed'}},
                    extra_functions={'main': {zero['layer']: {'implemented': True, 'group': 'Body'}}}))
payload = dict(schemaVersion=1, classes=classes,
               mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny', cases=cases)
with args.output.open('x', encoding='utf-8', newline='\n') as stream:
    stream.write(json.dumps(payload, separators=(',', ':')) + '\n')
print(f'Layer binding controls cases={len(cases)} operations={sum(len(c["operations"])+1 for c in cases)}')

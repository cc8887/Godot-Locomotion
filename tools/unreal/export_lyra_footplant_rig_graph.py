"""Read original FootPlant RigVM topology without saving or recompiling assets."""
import hashlib
import json
import os
from pathlib import Path

import unreal


root = Path(os.environ['LYRA_OUTPUT_ROOT'])
prefix = 'footplant_rig_graph_v1'
rig_path = '/Game/Characters/Heroes/Mannequin/Rig/CR_Mannequin_FootPlant.CR_Mannequin_FootPlant'
main_path = '/Game/Characters/Heroes/Mannequin/Animations/ABP_Mannequin_Base.ABP_Mannequin_Base'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
old_json = {str(p.relative_to(root)).replace('\\', '/'): sha(p)
            for p in root.rglob('*.json') if not p.name.startswith(prefix)}
content = Path(unreal.Paths.project_content_dir())
packages = dict(json.loads((root / 'montage_actions/catalog.json').read_bytes())['assetSha256'])
for path in (rig_path, main_path):
    packages[path] = sha(content / (path.split('.')[0].removeprefix('/Game/') + '.uasset'))


def protect():
    for path, digest in old_json.items():
        assert sha(root / path) == digest, path
    for path, digest in packages.items():
        file = content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')
        assert sha(file) == digest, path


def pin_row(pin):
    obj = pin.get_cpp_type_object()
    return {'path': pin.get_pin_path(), 'type': pin.get_cpp_type(),
            'typeObject': obj.get_path_name() if obj else '',
            'direction': str(pin.get_direction()), 'default': pin.get_default_value(),
            'subPins': [pin_row(p) for p in pin.get_sub_pins()]}


def node_row(node):
    row = {'name': node.get_name(), 'path': node.get_node_path(),
           'class': node.get_class().get_path_name(),
           'pins': [pin_row(p) for p in node.get_pins()]}
    if isinstance(node, unreal.RigVMTemplateNode):
        struct = node.get_script_struct()
        row['scriptStruct'] = struct.get_path_name() if struct else ''
    if isinstance(node, unreal.RigVMUnitNode):
        row['method'] = str(node.get_method_name())
    return row


protect()
blueprint = unreal.load_asset(rig_path)
assert blueprint is not None
graphs = []
seen = set()


def visit(graph):
    if not graph or graph.get_path_name() in seen:
        return
    seen.add(graph.get_path_name())
    graphs.append({'name': graph.get_graph_name(), 'path': graph.get_node_path(),
                   'objectPath': graph.get_path_name(),
                   'nodes': [node_row(n) for n in graph.get_nodes()],
                   'links': [{'source': l.get_source_pin().get_pin_path(),
                              'target': l.get_target_pin().get_pin_path()}
                             for l in graph.get_links()]})
    for child in graph.get_contained_graphs(False):
        visit(child)


for graph in blueprint.get_all_models():
    visit(graph)
visit(blueprint.get_local_function_library())
variables = []
for v in blueprint.get_member_variables():
    variables.append({'name': str(v.get_editor_property('name')),
                      'type': v.get_editor_property('cpp_type'),
                      'default': v.get_editor_property('default_value')})
hierarchy = blueprint.get_editor_property('hierarchy')
bones = []
for key in hierarchy.get_all_keys():
    parent = hierarchy.get_first_parent(key)
    transform = hierarchy.get_global_transform(key, True)
    t, q, s = transform.translation, transform.rotation, transform.scale3d
    bones.append({'name': str(key.name), 'type': str(key.type),
                  'parent': str(parent.name), 'parentType': str(parent.type),
                  'initialGlobal': {'translation': [t.x, t.y, t.z],
                                    'rotation': [q.x, q.y, q.z, q.w],
                                    'scale': [s.x, s.y, s.z]}})
main_cdo = unreal.get_default_object(unreal.load_class(None, main_path + '_C'))
defaults = {name: main_cdo.get_editor_property(name)
            for name in ('EnableControlRig', 'UseFootPlacement')}
protect()
value = {'schemaVersion': 1, 'rig': rig_path, 'main': main_path,
         'assetSha256': packages, 'previousFixtureSha256': old_json,
         'mainDefaults': defaults, 'variables': variables,
         'hierarchy': bones, 'graphs': graphs}
path = root / (prefix + '.json')
if path.exists():
    assert json.loads(path.read_bytes()) == value, 'Immutable RigVM graph capture differs'
else:
    path.write_text(json.dumps(value, separators=(',', ':'), allow_nan=False), encoding='utf-8')
unreal.log('LYRA_FOOTPLANT_RIG_GRAPH_OK graphs=%d nodes=%d hierarchy=%d variables=%d assets_saved=0' % (
    len(graphs), sum(len(g['nodes']) for g in graphs), len(bones), len(variables)))

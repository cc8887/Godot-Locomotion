"""Read Lyra's complete source/Montage notify contract without saving assets.

Timing comes from the native reader, not rounded T3D text. T3D preserves object
properties and nested MotionWarping modifiers; it is evidence, not executable DSL.
"""
import hashlib
import json
import os
import re
import tempfile
from collections import Counter
from pathlib import Path

import unreal


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        while block := stream.read(1024 * 1024):
            digest.update(block)
    return digest.hexdigest()


root = Path(os.environ['LYRA_OUTPUT_ROOT'])
repo = Path(__file__).resolve().parents[2]
content = Path(unreal.Paths.project_content_dir())
destination = root / 'notify_contract_v1.json'
dependency_names = ('logical_controls/catalog.json', 'main_lean/catalog.json',
                    'locomotion_extras/catalog.json', 'montage_actions/catalog.json',
                    'aiming_layer_v1_policy.json', 'source_nodes.json',
                    'montage_catalog_v2.json', 'linked_layer_contracts.json')
load = lambda name: json.loads((root / name).read_bytes())
dependencies = {name: sha(root / name) for name in dependency_names}
previous = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.json')
            if p != destination}
if destination.exists():
    previous = load(destination.name)['previousFixtureSha256']
packages = dict(load('rig_reference_v1_native.json')['assetSha256'])
project = Path(unreal.Paths.get_project_file_path())
project_hash = sha(project)


def package_file(path):
    if not path.startswith('/Game/'):
        raise ValueError('Expected project asset: ' + path)
    return content / (path.split('.')[0].removeprefix('/Game/') + '.uasset')


def protect():
    assert sha(project) == project_hash, 'Project descriptor changed'
    for path, digest in packages.items():
        assert sha(package_file(path)) == digest, path
    for name, digest in previous.items():
        assert sha(root / name) == digest, name


protect()
bindings = []
for name in dependency_names[:4]:
    bindings.extend(dict(catalog=name, slot=r['slot'], source=r['source'], target=r['target'])
                    for r in load(name)['entries'])
for space in load('aiming_layer_v1_policy.json')['spaces']:
    bindings.extend(dict(catalog='aiming_layer_v1_policy.json', slot=r['slot'],
                         source=r['source'], target=r['target']) for r in space['samples'])
# AimOffset policy repeats bindings already present in the logical catalog.
# Keep all catalog/slot evidence, with one physical target/object identity.
binding_sources = {}
for binding in bindings:
    assert binding_sources.setdefault(binding['target'], binding['source']) == binding['source'], binding
montages = load('montage_catalog_v2.json')
sources = {r['source'] for r in bindings} | set(montages['sequences'])
montage_paths = {r['path'] for r in montages['assets']}
spaces = {}
for owner, cls in load('source_nodes.json')['classes'].items():
    for node in cls['sources']:
        if node['kind'] in ('BlendSpacePlayer', 'BlendSpaceEvaluator') and node['asset']:
            spaces.setdefault(node['asset'], []).append(dict(owner=owner, node=node['nodeIndex']))
        for sample in node.get('samples', []):
            if sample['animation']:
                sources.add(sample['animation'])
        if node['kind'] in ('SequencePlayer', 'SequenceEvaluator') and node['asset']:
            sources.add(node['asset'])

class_objects = {}
blueprints = {}
fd, scratch_name = tempfile.mkstemp(prefix='lyra-notify-', suffix='.t3d',
                                  dir=repo / 'artifacts/lyra-analysis')
os.close(fd)
scratch = Path(scratch_name)


def object_text(obj):
    task = unreal.AssetExportTask()
    task.set_editor_property('object', obj)
    task.set_editor_property('filename', str(scratch))
    task.set_editor_property('automated', True)
    task.set_editor_property('prompt', False)
    task.set_editor_property('replace_identical', True)
    task.set_editor_property('exporter', unreal.ObjectExporterT3D())
    assert unreal.Exporter.run_asset_export_task(task), obj.get_path_name()
    assert not task.get_editor_property('errors'), task.get_editor_property('errors')
    text = scratch.read_text(encoding='utf-8-sig')
    assert text.strip(), obj.get_path_name()
    return text


def vec(v):
    return [v.x, v.y, v.z]


def value(v):
    if v is None or isinstance(v, (bool, int, float, str)):
        return v
    if isinstance(v, unreal.Object):
        return dict(path=v.get_path_name(), classPath=v.get_class().get_path_name())
    if hasattr(v, 'export_text'):
        return dict(nativeText=v.export_text())
    if hasattr(v, 'value'):
        return dict(name=str(v), value=v.value)
    return str(v)


def blueprint_contract(path):
    if path in blueprints:
        return blueprints[path]
    package = path.removesuffix('_C')
    packages.setdefault(package, sha(package_file(package)))
    bridge = unreal.BlueprintLispPythonBridge
    listed = bridge.list_graphs(package)
    assert listed.success and not listed.saved_package, (package, listed.message)
    variables = bridge.list_member_variables(package)
    assert variables.success and not variables.saved_package, (package, variables.message)
    members = json.loads(variables.dsl_text)
    graphs = {}
    for line in listed.dsl_text.splitlines():
        match = re.fullmatch(r'\[[^]]+\]\s+(.+)', line.strip())
        assert match, (package, line)
        name = match.group(1)
        result = bridge.export_graph_to_text(package, name, False, True)
        assert result.success and not result.saved_package, (package, name, result.message)
        graphs[name] = dict(dsl=result.dsl_text, warnings=list(result.warnings))
    contract = dict(package=package, variables=members, graphs=graphs)
    blueprints[path] = contract
    return contract


def payload(obj):
    cls = obj.get_class()
    path = cls.get_path_name()
    if path not in class_objects:
        class_objects[path] = dict(defaultObject=unreal.get_default_object(cls).get_path_name(),
                                  nativeText=object_text(unreal.get_default_object(cls)))
    get = obj.get_editor_property
    if path == '/Script/LyraGame.AnimNotify_LyraContextEffects':
        trace, vfx, audio = get('trace_properties'), get('vfx_properties'), get('audio_properties')
        rotation = get('rotation_offset')
        return dict(effect=str(get('effect').get_editor_property('tag_name')),
                    locationOffset=vec(get('location_offset')),
                    rotationOffset=[rotation.pitch, rotation.yaw, rotation.roll],
                    vfxScale=vec(vfx.get_editor_property('scale')),
                    volumeMultiplier=audio.get_editor_property('volume_multiplier'),
                    pitchMultiplier=audio.get_editor_property('pitch_multiplier'),
                    attached=get('attached'), socketName=str(get('socket_name')),
                    performTrace=get('perform_trace'),
                    traceChannel=value(trace.get_editor_property('trace_channel')),
                    traceEndOffset=vec(trace.get_editor_property('end_trace_location_offset')),
                    ignoreActor=trace.get_editor_property('ignore_actor'))
    if path == '/Script/Engine.AnimNotify_PlaySound':
        return {name: value(get(name)) for name in ('sound', 'volume_multiplier',
                    'pitch_multiplier', 'follow', 'attach_name')}
    if path == '/Script/MotionWarping.AnimNotifyState_MotionWarping':
        modifier = get('root_motion_modifier')
        return dict(rootMotionModifier=value(modifier),
                    modifierNativeText=object_text(modifier) if modifier is not None else '')
    if path.startswith('/Game/'):
        contract = blueprint_contract(path)
        rows = contract['variables']
        if isinstance(rows, dict):
            rows = rows['variables']
        return {row['variable_name']: value(get(row['variable_name'])) for row in rows}
    raise ValueError('Unbound notify class: ' + path)


def read(path):
    packages.setdefault(path, sha(package_file(path)))
    asset = unreal.load_asset(path)
    assert isinstance(asset, (unreal.AnimSequence, unreal.AnimMontage)), path
    native = json.loads(unreal.AlsSourceAnimationLibrary.read_source_notify_metadata(asset))
    assert native['source'] == path
    events = unreal.AnimationLibrary.get_animation_notify_events(asset)
    assert len(events) == len(native['events']), path
    for index, (event, row) in enumerate(zip(events, native['events'], strict=True)):
        get = event.get_editor_property
        notify, state = get('notify'), get('notify_state_class')
        assert not (notify is not None and state is not None)
        assert row['index'] == index and row['name'] == str(get('notify_name'))
        assert row['triggerTime'] == unreal.AnimationLibrary.get_anim_notify_event_trigger_time(event)
        assert (notify.get_class().get_path_name() if notify else '') == row['notifyClass']
        assert (state.get_class().get_path_name() if state else '') == row['notifyStateClass']
        obj = state if state is not None else notify
        row.update(notifyObject=notify.get_path_name() if notify else '',
                   stateObject=state.get_path_name() if state else '',
                   stateBehaviorFlags=state.get_editor_property('notify_state_behavior_flags') if state else 0,
                   tickMode=get('montage_tick_type').value,
                   storedDuration=unreal.AnimationLibrary.get_anim_notify_event_duration(event),
                   nativeText=event.export_text(),
                   objectNativeText=object_text(obj) if obj else '', payload=payload(obj) if obj else {})
    return native


try:
    assets = [read(path) for path in sorted(sources | montage_paths)]
    source_rows = {row['source']: row for row in assets}
    targets = [read(path) for path in sorted({r['target'] for r in bindings})]
    target_rows = {row['source']: row for row in targets}
    numeric_keys = ('index', 'name', 'notifyClass', 'notifyStateClass', 'time', 'duration',
                    'triggerTime', 'endTriggerTime', 'triggerTimeOffset', 'endTriggerTimeOffset',
                    'track', 'triggerWeightThreshold', 'triggerChance', 'filterType', 'filterLod',
                    'canBeFilteredViaRequest', 'triggerOnDedicatedServer', 'triggerOnFollower', 'branchingPoint')
    # Exact authored timing/policy preservation is checked for every target,
    # including sources absent from the old 189-clip notify export.
    for binding in bindings:
        a, b = source_rows[binding['source']], target_rows[binding['target']]
        assert a['length'] == b['length'], binding
        assert len(a['events']) == len(b['events']), binding
        for x, y in zip(a['events'], b['events'], strict=True):
            assert {k: x[k] for k in numeric_keys} == {k: y[k] for k in numeric_keys}, binding
            assert x['payload'] == y['payload'], binding
            assert x['stateBehaviorFlags'] == y['stateBehaviorFlags'] and x['tickMode'] == y['tickMode'], binding
    blend_spaces = []
    for path, owners in sorted(spaces.items()):
        packages.setdefault(path, sha(package_file(path)))
        data = json.loads(unreal.AlsSourceAnimationLibrary.read_source_sync_metadata(unreal.load_asset(path)))
        assert data['source'] == path
        assert all(row['sequence'] in source_rows for row in data['samples']), path
        blend_spaces.append(dict(path=path, owners=owners, sync=data))
    protect()
    result = dict(schemaVersion=1, dependencies=dependencies, assets=assets, targets=targets,
                  bindings=bindings, blendSpaces=blend_spaces, classes=class_objects, blueprints=blueprints,
                  assetSha256=packages, previousFixtureSha256=previous,
                  scope=dict(assetsSaved=0, runtimeIntegrated=False, nativeNotifyDispatchAccepted=False,
                             blueprintDslIsEvidence=True, audioPlaybackDeferred=True))
    if destination.exists():
        assert load(destination.name) == result, 'Immutable notify contract differs'
    else:
        with destination.open('x', encoding='utf-8', newline='\n') as stream:
            stream.write(json.dumps(result, separators=(',', ':'), allow_nan=False) + '\n')
    counts = Counter(row['notifyStateClass'] or row['notifyClass'] or '<named>'
                     for asset in assets for row in asset['events'])
    unreal.log('LYRA_NOTIFY_CONTRACT_OK assets=' + str(len(assets)) + ' targets=' + str(len(targets)) +
               ' events=' + str(sum(counts.values())) + ' classes=' + str(len(class_objects)) +
               ' blueprints=' + str(len(blueprints)) + ' packages=' + str(len(packages)) +
               ' previous=' + str(len(previous)) + ' assets_saved=0')
finally:
    scratch.unlink(missing_ok=True)

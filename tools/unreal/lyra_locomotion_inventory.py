"""Read complete nested locomotion closures and Blueprint defaults without saving."""
import hashlib
import json
from pathlib import Path
import unreal

HOOKS=('FullBody_IdleState','FullBody_StartState','FullBody_CycleState','FullBody_StopState','FullBody_PivotState',
       'FullBody_JumpStartState','FullBody_JumpStartLoopState','FullBody_JumpApexState','FullBody_FallLoopState','FullBody_FallLandState')

def export_inventory(root,base_packages):
    names=('source_nodes.json','runtime_graph.json','linked_layer_inventory.json','linked_layer_contracts.json','logical_controls/catalog.json')
    files={name:(root/name).read_bytes() for name in names}
    sources=json.loads(files['source_nodes.json'])['classes']
    catalog=json.loads(files['logical_controls/catalog.json'])
    sha=lambda b:hashlib.sha256(b).hexdigest()
    packages=dict(base_packages)
    targets={}
    for entry in catalog['entries']:
        targets.setdefault(entry['source'],[])
        if entry['target'] not in targets[entry['source']]:targets[entry['source']].append(entry['target'])
    def package(path):
        stem=path.split('.')[0]
        if stem.startswith('/Game/'):return Path(unreal.Paths.project_content_dir())/(stem[6:]+'.uasset')
        if stem.startswith('/Engine/'):return Path(unreal.Paths.engine_content_dir())/(stem[8:]+'.uasset')
        raise ValueError('Unsupported package mount: '+path)
    providers={};missing=set();closure_nodes=0;closure_sources=0;machine_links=0
    for profile in ('unarmed','pistol','rifle'):
        owner=sources[profile];cls=unreal.load_class(None,owner['class'])
        defaults=json.loads(unreal.AlsLyraGraphLibrary.read_animation_layer_defaults(cls))
        layers={}
        for hook in HOOKS:
            graph=json.loads(unreal.AlsLyraGraphLibrary.read_animation_layer_graph(cls,hook,True))
            indices={n['index'] for n in graph['nodes']}
            if len(indices)!=len(graph['nodes']) or graph['root'] not in indices:raise ValueError('Invalid closure identity')
            for node in graph['nodes']:
                for link in node['links']:
                    if link['index'] not in indices or link['propertyIndex']!=owner['nodeCount']-1-link['index']:
                        raise ValueError('Open compiled layer closure: '+hook)
                    machine_links+=link.get('compiledState',False)
            source_ids=sorted(n['nodeIndex'] for n in owner['sources'] if n['nodeIndex'] in indices)
            graph['sourceIndices']=source_ids
            layers[hook]=graph;closure_nodes+=len(indices);closure_sources+=len(source_ids)
        bindings={}
        for path in defaults['animationAssets']:
            original=unreal.load_asset(path)
            digest=sha(package(path).read_bytes())
            if path in packages and packages[path]!=digest:raise ValueError('Changed default binding package: '+path)
            packages[path]=digest
            if isinstance(original,unreal.AnimSequence):
                bindings[path]=targets.get(path,[])
                if not bindings[path]:missing.add(path)
        providers[profile]={'class':owner['class'],'nodeCount':owner['nodeCount'],'layers':layers,
            'defaults':defaults,'sequenceTargets':bindings}
    main_class=unreal.load_class(None,sources['main']['class'])
    main_defaults=json.loads(unreal.AlsLyraGraphLibrary.read_animation_layer_defaults(main_class))
    for path,digest in packages.items():
        if sha(package(path).read_bytes())!=digest:raise ValueError('Changed protected inventory package: '+path)
    payload={'schemaVersion':1,'dependencies':{n:sha(v) for n,v in files.items()},'assetSha256':packages,
        'providers':providers,'mainDefaults':main_defaults,'missingSequences':sorted(missing),
        'counts':{'providers':3,'layers':30,'closureNodes':closure_nodes,'sourceOccurrences':closure_sources,'compiledStateLinks':machine_links},
        'scope':'Complete static nested locomotion closures and Blueprint-owned defaults; no Idle/Pivot/Air runtime or full machine acceptance.'}
    path=root/'locomotion_layer_closures.json'
    if path.exists():
        if json.loads(path.read_bytes())!=payload:raise ValueError('Immutable nested inventory differs')
    else:path.write_text(json.dumps(payload,separators=(',',':')),encoding='utf-8')
    unreal.log('LYRA_LOCOMOTION_CLOSURES_NATIVE_OK '+json.dumps(payload['counts'])+' missing='+str(len(missing))+' assets_saved=0')
    return path.read_bytes()

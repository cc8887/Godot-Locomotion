"""Independent fixture/dependency/protected-package and joint traversal audit."""
import argparse
import hashlib
import json
import struct
from pathlib import Path

def verify(root, content):
    sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    load = lambda n: json.loads((root/n).read_bytes())
    native = load('main_source_scope_native.json')
    requests = load('main_source_scope_requests.json')
    if native['schemaVersion'] != 1 or native['requestSha256'] != sha(root/'main_source_scope_requests.json'):
        raise ValueError('Stale shared Main fixture')
    for name, digest in native['dependencies'].items():
        if sha(root/name) != digest: raise ValueError('Changed joint dependency: '+name)
    for path, digest in native['assetSha256'].items():
        if sha(content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')) != digest:
            raise ValueError('Changed protected package: '+path)
    graph = load('main_lean/composition_v3_policy.json')['graph']
    if {(r['index'], r['baseIndex'], r['additiveIndex']) for r in graph if r['layer'] in ('FullBody_StartState','FullBody_CycleState')} != {(13,11,12),(17,15,16)}:
        raise ValueError('Wrong compiled Main topology')
    single = lambda v: struct.unpack('<f',struct.pack('<f',v))[0]
    counts = dict.fromkeys(('frames','startPoses','cyclePoses','both','hidden','reverse','divergentClocks',
                           'rootAttributes','integerAttributes','curves','inertia','startSamples','cycleSamples'),0)
    identities = set()
    paths = {r['path'] for r in native['assets']}
    roots = load('main_source_scope_roots.json')
    if roots['requestSha256'] != native['requestSha256'] or roots['catalogSha256'] != sha(root/'logical_controls/catalog.json') or roots['assetSha256'] != native['assetSha256'] or set(roots['assets']) != paths:
        raise ValueError('Stale or incomplete common root codec inventory')
    old_roots = load('start_runtime_roots_v2.json')['assets']
    if any(roots['assets'][p] != v for p,v in old_roots.items()):
        raise ValueError('Changed previous Start compressed root')
    probes = sum(len(r['probes']) for r in roots['assets'].values())
    if probes != len(paths)*9: raise ValueError('Incomplete common root probes')
    for trace, authored in zip(native['traces'], requests['traces'], strict=True):
        identity = trace['profile'], trace['hz']
        if identity in identities or identity != (authored['profile'], authored['hz']):
            raise ValueError('Wrong native provider identity')
        identities.add(identity)
        for row, frame in zip(trace['frames'], authored['frames'], strict=True):
            state = row['observation']['after']
            if 'main' in frame or state['IsFirstUpdate'] or row['observation']['tailAfter']['mode'] != 0 or frame['observation']['snapshot'] != row['observation']['input']:
                raise ValueError('Joint roots bypassed complete Main/gather')
            if sorted(frame['order']) != [1,2]: raise ValueError('Invalid root traversal order')
            counts['frames'] += 1; counts['reverse'] += frame['order'][0] == 1
            counts['inertia'] += len(row['inertia'])
            a = frame['active']; b = frame['cycle']['active']
            counts['both'] += a and b; counts['hidden'] += not a and not b
            counts['divergentClocks'] += a and b and row['timeBits'] != row['cycle']['timeBits']
            for kind, root_row, active, direction in (
                ('start', row, a, 'LocalVelocityDirectionAngleWithOffset'),
                ('cycle', row['cycle'], b, 'LocalVelocityDirectionAngle')):
                if root_row['active'] != active or ('output' in root_row) != active:
                    raise ValueError('Wrong active/hidden actual root output')
                if not active: continue
                if root_row['asset'] not in paths or root_row['orientationAngle'] != single(state[direction]) or root_row['strideSpeed'] != single(state['DisplacementSpeed']):
                    raise ValueError('Stale or crossed Main bound pins')
                if root_row['lean']['pin'] != single(state['AdditiveLeanAngle']):
                    raise ValueError('Repeated or different Main rotation history')
                counts[kind+'Poses'] += 1; counts[kind+'Samples'] += len(root_row['lean']['samples'])
                output = root_row['output']
                if len(output['pose']) != 81 or len(output['attributes']) != 4:
                    raise ValueError('Incomplete original root output')
                counts['integerAttributes'] += len(output['attributes']); counts['curves'] += len(output['curves'])
                if 'rootMotion' in output:
                    r = output['rootMotion']
                    if (r['name'],r['bone'],r['namespace'],r['type']) != ('RootMotionDelta','root','bone','/Script/Engine.TransformAnimationAttribute'):
                        raise ValueError('Changed generated root identity')
                    counts['rootAttributes'] += 1
    if identities != {(p,h) for p in ('unarmed','pistol','rifle') for h in (30,60,120)} or any(
        counts[k] != v for k,v in {'frames':3780,'startPoses':3150,'cyclePoses':3150,'both':2709,'hidden':189}.items()):
        raise ValueError('Incomplete joint native coverage: '+str(counts))
    if counts['reverse'] == 0 or counts['divergentClocks'] == 0:
        raise ValueError('Traversal/history branches not covered')
    return {'status':'pass','packages':len(native['assetSha256']),'assets':len(paths),'rootProbes':probes,'counts':counts,
            'files':{n:{'sha256':sha(root/n),'bytes':(root/n).stat().st_size} for n in ('main_source_scope_requests.json','main_source_scope_native.json','main_source_scope_roots.json')},
            'scope':native['scope'],'production':False,'wholeMachine':False}

if __name__ == '__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=Path('../GASP58/Content'))
    parser.add_argument('--output',type=Path,default=Path('artifacts/lyra-analysis/main-source-scope-resource-verification.json'))
    args=parser.parse_args();report=verify(args.root,args.content)
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LYRA_MAIN_SOURCE_SCOPE_VERIFY_OK '+json.dumps(report['counts']))

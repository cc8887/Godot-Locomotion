"""Three-root common Main/source capture, static codecs and package protection."""
import argparse
import base64
import hashlib
import json
from pathlib import Path

sha=lambda b:hashlib.sha256(b).hexdigest()

def verify(root,content,prefix='main_source_stop',state_roots=False):
    load=lambda name:json.loads((root/name).read_bytes())
    native=load(prefix+'_native.json');requests=load(prefix+'_requests.json');roots=load(prefix+'_roots.json')
    if native['requestSha256']!=sha((root/(prefix+'_requests.json')).read_bytes()) or roots['requestSha256']!=native['requestSha256']:
        raise ValueError('Stale common request identity')
    if roots['catalogSha256']!=sha((root/'logical_controls/catalog.json').read_bytes()) or roots['assetSha256']!=native['assetSha256']:
        raise ValueError('Stale common compressed roots')
    for name,digest in native['dependencies'].items():
        if sha((root/name).read_bytes())!=digest:raise ValueError('Changed dependency: '+name)
    for path,digest in native['assetSha256'].items():
        if sha((content/(path.split('.')[0].removeprefix('/Game/')+'.uasset')).read_bytes())!=digest:
            raise ValueError('Changed package: '+path)
    paths=[a['path'] for a in native['assets']]
    if paths!=requests['sequencePaths'] or len(paths)!=114 or set(paths)!=set(roots['assets']):
        raise ValueError('Incomplete common source inventory')
    prior=load('main_source_scope_roots.json')['assets'];stop=load('stop_runtime_roots.json')['assets']
    for path,data in roots['assets'].items():
        if data!=prior.get(path,stop.get(path)):raise ValueError('Changed settled root codec')
        if not base64.b64decode(data['payload'],validate=True) or len(data['probes'])!=9 or len(data['channels'])!=3:
            raise ValueError('Invalid compressed root metadata')
    counts=dict.fromkeys(('frames','startPoses','cyclePoses','stopPoses','both','allThree','stopOnly','hidden'),0)
    extra=dict.fromkeys(('inertia','stopAccumulating','stopBlendingOut','modeFeedback','sourceRootAttributes','absentRootAttributes'),0)
    if state_roots:extra.update(dict.fromkeys(('stateUpdates','holdFinal','holdFeedback','startSuppressed','zeroPreviousStart','holdThenAccumulate','accumulateThenHold'),0))
    orders=set();identities=set();stop_assets=set()
    for trace,authored in zip(native['traces'],requests['traces'],strict=True):
        identity=(trace['profile'],trace['hz'])
        if identity in identities or identity!=(authored['profile'],authored['hz']):raise ValueError('Reordered provider trace')
        identities.add(identity);previous_mode=0
        if state_roots:
            expected=[(10,1,'UpdateStartState'),(14,2,'None'),(18,3,'UpdateStopState')]
            if [(b['node'],b['state'],b['update']) for b in trace['stateRootBindings']]!=expected:
                raise ValueError('Changed actual state-root callback binding')
        for row,frame in zip(trace['frames'],authored['frames'],strict=True):
            observation=row['observation'];st=row['stop']
            if frame['observation']['mode']!=previous_mode or observation['tailBefore']['mode']!=previous_mode:
                raise ValueError('Main did not consume the actual previous graph mode')
            previous_mode=st['rootYawModeAfterGraph']
            if observation['tailAfter']['mode']!=0 or observation['after']['IsFirstUpdate'] or frame['observation']['snapshot']!=observation['input']:
                raise ValueError('Incomplete original Main macro/gather')
            extra['modeFeedback']+=frame['observation']['mode']==2
            if state_roots:
                import struct
                sr=frame['stateRoots'];nr=row['stateRoots']
                if sr['current']!=nr['current'] or sr['current']!=st['machineCurrent']:
                    raise ValueError('Different actual machine observation')
                for key in ('previousStartWeight','previousCycleWeight','previousStopWeight'):
                    if struct.pack('f',sr[key])!=struct.pack('f',nr[key]):raise ValueError('Different previous state weight')
                updates=row['stateUpdates'];actual_order=[v['root'] for v in updates]
                active_order=[
                    n for n in frame['order'] if (frame['active'] if n==2 else frame['cycle']['active'] if n==1 else frame['stop']['active'])]
                if actual_order!=active_order:raise ValueError('Changed state callback traversal order')
                before=observation['tailAfter']['mode']
                for update in updates:
                    if update['modeBefore']!=before:raise ValueError('State callbacks lost the shared mode field')
                    before=update['modeAfter']
                    extra['holdThenAccumulate']+=update['modeBefore']==1 and before==2
                    extra['accumulateThenHold']+=update['modeBefore']==2 and before==1
                if before!=previous_mode:raise ValueError('State callback result was not fed back')
                extra['stateUpdates']+=len(updates);extra['holdFinal']+=previous_mode==1
                extra['holdFeedback']+=frame['observation']['mode']==1
                extra['startSuppressed']+=frame['active'] and sr['previousStartWeight']>0 and sr['current']!=1
                extra['zeroPreviousStart']+=frame['active'] and sr['previousStartWeight']==0
            order=tuple(frame['order'])
            if sorted(order)!=[1,2,3]:raise ValueError('Missing/repeated traversal root')
            orders.add(order)
            a='output' in row;b='output' in row['cycle'];c='output' in st
            if (a,b,c)!=(frame['active'],frame['cycle']['active'],frame['stop']['active']):
                raise ValueError('Wrong native relevance/evaluation')
            counts['frames']+=1;counts['startPoses']+=a;counts['cyclePoses']+=b;counts['stopPoses']+=c
            counts['both']+=a and b;counts['allThree']+=a and b and c;counts['stopOnly']+=c and not a and not b;counts['hidden']+=not a and not b and not c
            extra['inertia']+=len(row['inertia'])
            if c:
                stop_assets.add(st['asset']);extra['stopAccumulating']+=previous_mode==2;extra['stopBlendingOut']+=previous_mode==0
            for output in ([row['output']] if a else [])+([row['cycle']['output']] if b else [])+([st['output']] if c else []):
                if len(output['pose'])!=81 or len(output['attributes'])!=4:raise ValueError('Incomplete logical pose/data')
                if 'rootMotion' in output:
                    rm=output['rootMotion']
                    if (rm['name'],rm['bone'],rm['namespace'],rm['type'])!=('RootMotionDelta','root','bone','/Script/Engine.TransformAnimationAttribute'):
                        raise ValueError('Wrong typed root identity')
                    extra['sourceRootAttributes']+=1
                else:extra['absentRootAttributes']+=1
    if counts!=native['counts'] or counts['frames']!=3780 or counts['startPoses']!=3150 or counts['cyclePoses']!=3150 or \
       not counts['allThree'] or not counts['stopOnly'] or not counts['hidden'] or len(orders)!=6 or not extra['modeFeedback']:
        raise ValueError('Incomplete common roots coverage: '+str(counts))
    if state_roots and (extra['stateUpdates']!=counts['startPoses']+counts['cyclePoses']+counts['stopPoses'] or
        not all(extra[k] for k in ('holdFinal','holdFeedback','startSuppressed','zeroPreviousStart','holdThenAccumulate','accumulateThenHold'))):
        raise ValueError('Incomplete state callback/Hold feedback coverage')
    names=tuple(prefix+'_'+suffix+'.json' for suffix in ('requests','native','roots'))
    return {'status':'pass_component','counts':counts,'extra':extra,'stopAssets':len(stop_assets),'orders':[list(v) for v in sorted(orders)],
            'packages':len(native['assetSha256']),'assets':len(paths),'rootProbes':len(paths)*9,
            'files':{n:{'sha256':sha((root/n).read_bytes()),'bytes':(root/n).stat().st_size} for n in names},
            'production':False,'wholeMachine':False,'scope':('Actual Start10/Cycle14/Stop18 state callbacks in one Main/provider/Sync, including Hold/Accumulate feedback; ' if state_roots else 'Common Main/provider/Sync for Start/Cycle/Stop, including actual previous graph mode feedback; ')+
            'root state observations, weights and order are explicit. No full transition selection, final state blend or production acceptance.'}

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=Path('../GASP58/Content'))
    parser.add_argument('--output',type=Path,default=Path('artifacts/lyra-analysis/main-source-stop-resource-verification.json'))
    args=parser.parse_args();report=verify(args.root,args.content)
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LYRA_MAIN_SOURCE_STOP_VERIFY_OK '+json.dumps({'counts':report['counts'],'extra':report['extra']}))

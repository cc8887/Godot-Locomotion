"""Check the worker-update boundary in the actual per-call native snapshots."""
import argparse
import gc
import json
from pathlib import Path
from locomotion_paths import engine_path

from verify_lyra_multi_layer_native import EVIDENCE, ROOT, read, sha


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--tag',default='multi-layer-v3-30-full')
    parser.add_argument('--output',required=True)
    args=parser.parse_args()
    assert all(value.replace('-','').replace('_','').isalnum() for value in (args.tag,args.output))
    output=EVIDENCE/f'{args.output}-worker-boundary.json'
    assert not output.exists()
    request=read(EVIDENCE/f'whole-main-{args.tag}-request.json')
    manifest=read(EVIDENCE/f'whole-main-{args.tag}-native.json')
    names=('HipFireUpperBodyOverrideWeight','AimOffsetBlendWeight','TimeFalling','LandRecoveryAlpha')
    trajectories=[]
    for index,authored in enumerate(request['traces']):
        if authored['layout']!='per-call':continue
        entry=manifest['traceFiles'][index]
        path=EVIDENCE/entry['file']
        assert sha(path)==entry['sha256']
        trace=read(path)
        assert trace['instanceCount']==14
        owners={c['function']:c['owner'] for c in trace['calls']}
        dormant=changed=0;witnesses=[]
        for frame_index,frame in enumerate(trace['frames']):
            visited={owners[u['hook']] for u in frame['updates'] if u['hook'] in owners}
            for before,after in zip(frame['instancesBefore'],frame['instancesUpdated'],strict=True):
                assert before['owner']==after['owner']
                differences=[name for name in names if before['fields'][name]!=after['fields'][name]]
                if before['owner'] not in visited:
                    assert not differences,(authored['profile'],frame_index,before['owner'],differences)
                    dormant+=1
                    if len(witnesses)<3 and any(before['fields'][n]!=0 for n in names):
                        witnesses.append(dict(frame=frame_index,owner=before['owner'],visitedOwners=sorted(visited),
                                              before={n:before['fields'][n] for n in names},
                                              updated={n:after['fields'][n] for n in names}))
                elif differences:changed+=1
        assert dormant>len(trace['frames']) and changed>0 and witnesses
        trajectories.append(dict(profile=authored['profile'],hz=authored['hz'],frames=len(trace['frames']),
                                 dormantOwnerFrames=dormant,changedVisitedOwnerFrames=changed,
                                 witnesses=witnesses,nativeSha256=entry['sha256']))
        del trace
        gc.collect()
    assert len(trajectories)==3
    proxy=engine_path('Engine/Source/Runtime/Engine/Private/Animation/AnimInstanceProxy.cpp')
    linked=proxy.with_name('AnimNode_LinkedAnimGraph.cpp')
    assert 'if(FrameCounterForUpdate != GFrameCounter)' in proxy.read_text(encoding='utf-8-sig')
    assert 'Proxy.UpdateAnimation_WithRoot(NewContext, LinkedRoot, GetDynamicLinkFunctionName())' in linked.read_text(encoding='utf-8-sig')
    result=dict(auditPassed=True,trajectories=trajectories,
                engineSources={str(p):sha(p) for p in (proxy,linked)},verifierSha256=sha(Path(__file__)),
                allInstancesWorkerUpdateRejected=True,fullPrivateFieldParity=False,goalComplete=False)
    with output.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(result,stream,ensure_ascii=False,indent=2,allow_nan=False);stream.write('\n')
    print(output)


if __name__=='__main__':main()

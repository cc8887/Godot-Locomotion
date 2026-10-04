"""Inventory every captured linked field at the original update boundaries."""
import argparse
import gc
import json
from collections import Counter, defaultdict
from verify_lyra_multi_layer_native import EVIDENCE, read, sha


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--reference',default='multi-layer-v3-30-full')
    parser.add_argument('--output',required=True)
    args=parser.parse_args()
    assert all(v.replace('-','').replace('_','').isalnum() for v in (args.reference,args.output))
    output=EVIDENCE/f'{args.output}-private-fields.json';assert not output.exists()
    request=read(EVIDENCE/f'whole-main-{args.reference}-request.json')
    manifest=read(EVIDENCE/f'whole-main-{args.reference}-native.json')
    reports=[]
    for ti,authored in enumerate(request['traces']):
        if authored['layout']!='per-call':continue
        entry=manifest['traceFiles'][ti];path=EVIDENCE/entry['file'];assert sha(path)==entry['sha256']
        trace=read(path);owners={c['function']:c['owner'] for c in trace['calls']}
        counts=defaultdict(Counter);witnesses=defaultdict(list)
        names=set(trace['frames'][0]['instancesBefore'][0]['fields'])
        for fi,frame in enumerate(trace['frames']):
            visited={owners[u['hook']] for u in frame['updates'] if u['hook'] in owners}
            for before,updated,after in zip(frame['instancesBefore'],frame['instancesUpdated'],frame['instancesAfter'],strict=True):
                assert before['owner']==updated['owner']==after['owner']
                assert set(before['fields'])==set(updated['fields'])==set(after['fields'])==names
                kind='visited' if before['owner'] in visited else 'dormant'
                for name in sorted(names):
                    counts[name][kind+'OwnerFrames']+=1
                    if before['fields'][name]!=updated['fields'][name]:
                        counts[name][kind+'UpdateChanges']+=1
                        if len(witnesses[name])<3:
                            witnesses[name].append(dict(frame=fi,owner=before['owner'],visited=before['owner'] in visited,
                                                        before=before['fields'][name],updated=updated['fields'][name],after=after['fields'][name]))
                    if updated['fields'][name]!=after['fields'][name]:counts[name]['evaluateChanges']+=1
        fields={name:dict(counts[name],witnesses=witnesses[name]) for name in sorted(names)}
        print(authored['profile'], 'fields',len(fields),'dormant updates',[(n,v['dormantUpdateChanges']) for n,v in fields.items() if v.get('dormantUpdateChanges')])
        print('visited updates',[(n,v['visitedUpdateChanges']) for n,v in fields.items() if v.get('visitedUpdateChanges')])
        reports.append(dict(profile=authored['profile'],hz=authored['hz'],frames=len(trace['frames']),
                            fields=fields,nativeSha256=entry['sha256'],
                            authoredFiringFrames=sum(f['observation']['firing'] for f in authored['frames']),
                            authoredAdsFrames=sum(f['observation']['ads'] for f in authored['frames'])))
        del trace;gc.collect()
    assert len(reports)==3
    result=dict(auditPassed=True,reference=args.reference,trajectories=reports,fullPrivateFieldParity=False,
                goalComplete=False,verifierSha256=sha(__import__('pathlib').Path(__file__)))
    with output.open('x',encoding='utf-8',newline='\n') as stream:
        json.dump(result,stream,ensure_ascii=False,indent=2,allow_nan=False);stream.write('\n')
    print(output)


if __name__=='__main__':main()

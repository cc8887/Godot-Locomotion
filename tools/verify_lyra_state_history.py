"""Nested closure/default inventory and actual Start relevance history gates."""
import argparse
import hashlib
import json
from pathlib import Path
from verify_lyra_main_source_stop import verify

def verify_inventory(root,content,engine_content):
    load=lambda n:json.loads((root/n).read_bytes())
    data=load('locomotion_layer_closures.json');sources=load('source_nodes.json')['classes'];runtime=load('runtime_graph.json')['classes']
    sha=lambda b:hashlib.sha256(b).hexdigest()
    for name,digest in data['dependencies'].items():
        if sha((root/name).read_bytes())!=digest:raise ValueError('Changed inventory dependency: '+name)
    for path,digest in data['assetSha256'].items():
        stem=path.split('.')[0]
        directory=content if stem.startswith('/Game/') else engine_content if stem.startswith('/Engine/') else None
        if directory is None or sha((directory/(stem.split('/',2)[2]+'.uasset')).read_bytes())!=digest:
            raise ValueError('Changed original default package: '+path)
    totals={'providers':0,'layers':0,'closureNodes':0,'sourceOccurrences':0,'compiledStateLinks':0};missing=set()
    for name,provider in data['providers'].items():
        original=sources[name];totals['providers']+=1
        if provider['class']!=original['class'] or provider['nodeCount']!=original['nodeCount']:raise ValueError('Changed source layout')
        expected={g['name']:set(g['players']) for g in original['graphs']}
        callbacks={c['nodeIndex']:c['functions'] for c in original['callbacks']}
        for machine in runtime[name]['machines']:
            hook='FullBody_IdleState' if machine['machineName'] in ('IdleSM','IdleStance') else 'FullBody_PivotState' if machine['machineName']=='PivotSM' else None
            if hook:
                for state in machine['states']:expected.setdefault(hook,set()).update(state['playerNodeIndices'])
        for hook,graph in provider['layers'].items():
            nodes={n['index']:n for n in graph['nodes']};totals['layers']+=1;totals['closureNodes']+=len(nodes)
            if len(nodes)!=len(graph['nodes']) or graph['root'] not in nodes or graph['rootPropertyIndex']!=original['nodeCount']-1-graph['root']:
                raise ValueError('Invalid compiled root identity')
            for i,node in nodes.items():
                if node['functions']!=callbacks.get(i,dict.fromkeys(('initialUpdate','becomeRelevant','update'),'None')):
                    raise ValueError('Changed callback on closure node')
                for link in node['links']:
                    if link['index'] not in nodes or link['propertyIndex']!=original['nodeCount']-1-link['index']:
                        raise ValueError('Open nested pose closure')
                    totals['compiledStateLinks']+=link.get('compiledState',False)
            actual=sorted(s['nodeIndex'] for s in original['sources'] if s['nodeIndex'] in nodes)
            if actual!=graph['sourceIndices'] or set(actual)!=expected.get(hook,set()):raise ValueError('Incomplete nested source ownership: '+hook)
            totals['sourceOccurrences']+=len(actual)
        missing.update(path for path,targets in provider['sequenceTargets'].items() if not targets)
        pivot=provider['layers']['FullBody_PivotState']
        pivot_callbacks=[n for n in pivot['nodes'] if n['functions']['update']=='UpdatePivotAnim']
        if len(pivot_callbacks)!=2:raise ValueError('Pivot lost an evaluator occurrence')
    if totals!=data['counts'] or totals['layers']!=30 or sorted(missing)!=data['missingSequences']:raise ValueError('Incomplete closure inventory')
    return {'counts':totals,'packages':len(data['assetSha256']),'missingSequences':sorted(missing)}

def verify_history(root,content):
    report=verify(root,content,prefix='main_state_history',state_roots=True)
    native=json.loads((root/'main_state_history_native.json').read_bytes())
    counts={'entries':0,'changed':0,'retainedAgainstLiveDirection':0}
    for trace in native['traces']:
        binding=trace['stateRootBindings'][0]
        if binding['becomeRelevant']!='SetUpStartState':raise ValueError('Start relevance callback was not bound')
        previous=0
        for row in trace['frames']:
            if row['startDirectionBeforeGraph']!=previous:raise ValueError('Start latch did not retain the previous graph value')
            for update in row['stateUpdates']:
                if update['startBefore']!=previous:raise ValueError('Source order lost shared StartDirection')
                if update['startBecameRelevant']:
                    if update['root']!=2:raise ValueError('Foreign Start relevance occurrence')
                    counts['entries']+=1
                previous=update['startAfter']
            if previous!=row['startDirectionAfterGraph']:raise ValueError('Wrong post-graph Start direction')
            counts['changed']+=previous!=row['startDirectionBeforeGraph']
            counts['retainedAgainstLiveDirection']+='output' in row and previous!=row['observation']['after']['LocalVelocityDirection']
    if not all(counts.values()):raise ValueError('Incomplete Start latch coverage')
    report['startHistory']=counts
    report['scope']='Actual Start10/Cycle14/Stop18 callbacks in one Main/provider/Sync, including Start relevance direction history and Hold/Accumulate feedback. State observations, weights and order remain explicit; no complete transition selection, final state blend or production acceptance.'
    return report

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=Path('../GASP58/Content'))
    parser.add_argument('--engine-content',type=Path,default=Path('../UE_5.8/Engine/Content'))
    parser.add_argument('--output',type=Path,default=Path('artifacts/lyra-analysis/state-history-resource-verification.json'))
    args=parser.parse_args();report=verify_history(args.root,args.content)
    report['nestedInventory']=verify_inventory(args.root,args.content,args.engine_content)
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LYRA_STATE_HISTORY_VERIFY_OK '+json.dumps({'counts':report['counts'],'startHistory':report['startHistory'],'nestedInventory':report['nestedInventory']}))

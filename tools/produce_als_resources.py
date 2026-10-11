"""Compile/run the ALS Kimodo DAG; run includes asset conversion and Godot checks."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# Works from any cwd; graph imports the existing contract inventory module.
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from tools.kimodo_pipeline.graph import build_graph, mermaid, read_json, load_rules, topological_order


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command',choices=('plan','run','inspect','inputs'))
    parser.add_argument('--contracts',type=Path,default=ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
    parser.add_argument('--rules',type=Path)
    parser.add_argument('--output',type=Path,default=ROOT/'artifacts/kimodo_als/production')
    parser.add_argument('--kimodo-repo',type=Path)
    parser.add_argument('--python',type=Path)
    parser.add_argument('--bindings',type=Path,help='Node id -> approved source path (relative to this JSON)')
    parser.add_argument('--upstream-receipts',type=Path,action='append',default=[],help='Explicit completed upstream receipts to reuse with byte/rig checks')
    parser.add_argument('--base-pack',type=Path,help='Retain deliveries from this diagnostic pack and generate its missing derived resources')
    parser.add_argument('--runtime-case',action='append',help='Exact related runtime scenario; omitted runs the full matrix')
    parser.add_argument('--target',action='append',help='Exact node id; includes all transitive prerequisites')
    parser.add_argument('--scope',choices=('all','non-prop','mantle','air','derived','run'),default='non-prop',help='Non-prop closure, independent mantle/air/derived/run lane, or explicit diagnostic nodes')
    parser.add_argument('--keep-going',action='store_true',help='Attempt independent branches; failed prerequisites still block dependents')
    parser.add_argument('--authoring-only',action='store_true',help='Explicit diagnostic exception: stop before conversion/runtime')
    parser.add_argument('--pack-output',type=Path)
    parser.add_argument('--godot',type=Path,default=ROOT/'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe')
    parser.add_argument('--dotnet',type=Path,default=Path.home()/'.dotnet/dotnet.exe')
    parser.add_argument('--offline-deps',type=Path,default=ROOT/'artifacts/kimodo_als/offline-python-deps')
    args=parser.parse_args()
    if args.rules is None:
        args.rules=ROOT/'tools/kimodo_pipeline'/({'air':'air_rules.json','run':'run_rules.json'}.get(args.scope,'directional_gait_rules.json'))
    if args.scope=='air':
        from tools.kimodo_pipeline.air import build_graph as build_air_graph
        graph=build_air_graph(read_json(args.contracts),load_rules(args.rules))
    else:graph=build_graph(read_json(args.contracts),load_rules(args.rules))
    if args.upstream_receipts or args.scope=='derived':
        from tools.kimodo_pipeline.derived import reuse_upstream
        graph=reuse_upstream(graph,args.upstream_receipts,derived_only=args.scope=='derived')
    if args.scope in ('non-prop','mantle','run'):
        from tools.kimodo_pipeline.scope import non_prop_targets
        if args.target:raise ValueError('Use either a named scope or explicit --target')
        args.target=non_prop_targets(read_json(args.contracts))
        if args.scope=='mantle':
            args.target=[key for key in args.target if graph['nodes'][key]['params']['contract']['family']=='mantle']
            if not args.target:raise ValueError('No mantle assets in the selected non-prop contract')
        elif args.scope=='run':
            from tools.kimodo_pipeline.scope import run_targets
            args.target=run_targets(read_json(args.contracts))
            if any(graph['nodes'][key]['params']['contract'].get('targetGaitPolicy')!='als-forward-v1'
                   for key in args.target):
                raise ValueError('Run scope requires authoring and target gait gates for every direction')
            graph['pipeline']='run'
    elif args.scope=='air':
        from tools.kimodo_pipeline.air import targets
        if args.target:raise ValueError('Use either the Air scope or explicit --target')
        args.target=targets(read_json(args.contracts))
    elif args.scope=='derived':
        if args.target or not args.base_pack or not args.upstream_receipts:
            parser.error('Derived scope requires --base-pack and --upstream-receipts, without --target')
        from tools.kimodo_pipeline.derived import targets
        args.target=targets(graph,read_json(args.contracts),read_json(args.base_pack))
        if not args.target:parser.error('No missing derived resources in the selected pack')
    order=topological_order(graph['nodes'],args.target)
    if args.command=='inspect':
        print(json.dumps({'summary':graph['summary'],'selectedOrder':order,
                          'externalInputs':[k for k in order if graph['nodes'][k]['op']=='external']},ensure_ascii=False,indent=2));return
    if args.command=='plan':
        args.output.mkdir(parents=True,exist_ok=True)
        (args.output/'graph.json').write_text(json.dumps(graph,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        (args.output/'graph.mmd').write_text(mermaid(graph),encoding='utf-8')
        print(json.dumps(graph['summary'],ensure_ascii=False,indent=2));return
    if args.command=='inputs':
        args.output.mkdir(parents=True,exist_ok=True)
        requests={key:graph['nodes'][key]['params'] for key in order if graph['nodes'][key]['op']=='external'}
        path=args.output/'authoring-input-requests.json'
        path.write_text(json.dumps(requests,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print(json.dumps({'inputs':len(requests),'path':str(path)},ensure_ascii=False));return
    if not args.authoring_only:
        if args.scope not in ('non-prop','mantle','air','derived','run'):parser.error('Full pipeline requires a named production scope; explicit graph diagnostics use --authoring-only')
        if not args.kimodo_repo:parser.error('Full pipeline requires --kimodo-repo')
        for path in (args.godot,args.dotnet,args.offline_deps/'ufbx/__init__.py'):
            if not path.is_file():parser.error('Full-pipeline dependency missing: '+str(path))
    from tools.kimodo_pipeline.runner import Runner, resolve_bindings
    from tools.kimodo_pipeline.runner import file_hash
    evidence={entry['file']:entry['sha256'] for row in read_json(args.contracts)['assets'] for entry in row['evidence']}
    for relative,expected in evidence.items():
        path=ROOT/relative
        if not path.is_file() or file_hash(path)!=expected:
            raise ValueError(f'Contract source changed/missing: {relative}; refresh the asset contract inventory first')
    runner=Runner(graph,args.output,args.kimodo_repo,args.python,resolve_bindings(args.bindings))
    if args.keep_going or not args.authoring_only:
        from tools.kimodo_pipeline.batch import run_batch
        result=run_batch(runner,args.target or order)
    else:
        result=runner.run(args.target)
    print(json.dumps(result,ensure_ascii=False,indent=2))
    if not args.authoring_only:
        from tools.kimodo_pipeline.full_pipeline import complete_run
        result=complete_run(runner,result,pack=args.pack_output,godot=args.godot,dotnet=args.dotnet,deps=args.offline_deps,
            runtime_cases=args.runtime_case,base_pack=args.base_pack)
        print(json.dumps(result,ensure_ascii=False,indent=2))
        if result['status']!='pipeline_complete':raise SystemExit(1)
    elif args.keep_going and result['status']!='authoring_complete':
        raise SystemExit(1)


if __name__=='__main__':main()

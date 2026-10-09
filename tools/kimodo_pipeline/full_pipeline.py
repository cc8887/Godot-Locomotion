"""Default production orchestration: delivered sources -> assets -> Godot checks."""
import os
import sys
import subprocess
from pathlib import Path
from datetime import datetime
from .graph import read_json
from .runner import write_json, file_hash
from .publish import ROOT, finalize_requests


def verify_runtime(pack, output, godot, dotnet, python):
    """Keep fresh logs and attempt the matrix even if diagnostic acceptance fails."""
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    report=output/'runtime-result.json'
    if report.exists():raise ValueError('Runtime evidence already exists; use a fresh output directory')
    resource='res://'+Path(pack).resolve().relative_to(ROOT).as_posix()
    env=os.environ.copy()
    for key in ('ALS_ASSET_ROOT','ALS_RAW_SOURCE_GENERATED_DIR','ALS_REQUIRE_KIMODO_SOURCE'):
        env.pop(key,None)
    env['ALS_RESOURCE_PACK_PATH']=resource
    checks=[]
    def update_viewer():
        from .viewer_pack import export_pack
        provenance=Path(pack).parent/'pipeline-provenance.json'
        config=read_json(provenance) if provenance.exists() else {}
        return export_pack(Path(pack),batch_id=config.get('batchId'),make_latest=True)
    def run(name,command,*,marker=None,extra_env=None,timeout=2400):
        log=output/(name+'.log')
        with log.open('w',encoding='utf-8') as stream:
            try:
                process=subprocess.run([str(x) for x in command],cwd=ROOT,env=dict(env,**(extra_env or {})),
                    stdout=stream,stderr=subprocess.STDOUT,timeout=timeout)
                code=process.returncode
            except subprocess.TimeoutExpired:
                code=-1;stream.write('\nPIPELINE_TIMEOUT\n')
        text=log.read_text(encoding='utf-8',errors='replace')
        entry=dict(name=name,exitCode=code,log=str(log),marker=marker,
            passed=code==0 and (not marker or marker in text))
        checks.append(entry);write_json(report,dict(status='running',pack=resource,checks=checks))
        print(('PASS ' if entry['passed'] else 'FAIL ')+name,flush=True)
        return entry['passed']
    built=run('build',[dotnet,'build','GodotALS.csproj','-p:Optimize=true'])
    imported=run('godot-import',[godot,'--headless','--path',ROOT,'--editor','--import'])
    if built and imported:
        run('p2b',[godot,'--headless','--path',ROOT,'res://scenes/tests/p2b_import.tscn'],marker='P2B_IMPORT_OK')
        requests=output/'requests';requests.mkdir()
        request_ok=True
        for domain,flag in [('recovery_movement',''),('aim','--aim-source'),('overlay','--overlay-source'),
                            ('ragdoll','--ragdoll-source'),('stop','--stop-source'),('overlay_prop','--prop-source')]:
            command=[godot,'--headless','--path',ROOT,'res://scenes/tests/raw_sequence_source_request.tscn']
            if flag:command+=['--',flag]
            okay=run('request-'+domain,command,marker='RAW_SOURCE_REQUEST_OK',
                extra_env={'ALS_RAW_SOURCE_REQUEST_OUTPUT':str(requests/(domain+'-request.json'))})
            request_ok=okay and request_ok
        if request_ok:
            finalize_requests(Path(pack),requests)
            update_viewer()
        matrix=output/'e2e-matrix'
        matrix_command=[python,ROOT/'scripts/verify-als-nonprop-e2e.py','--pack',resource,
            '--output',matrix,'--godot',godot,'--headless-fixed-fps','60']
        if read_json(pack).get('missingNonPropSources'):matrix_command.append('--diagnostic-partial-pack')
        run('e2e-matrix',matrix_command,marker='ALS_NON_PROP_E2E_OK',timeout=7200)
    else:
        checks.append(dict(name='e2e-matrix',passed=False,status='blocked',reason='Godot build/import failed'))
    result=dict(status='runtime_passed' if all(c['passed'] for c in checks) else 'runtime_failed',
        pack=resource,packSha256=file_hash(pack),checks=checks,productionReady=False,activated=False)
    write_json(report,result)
    # Request finalization changes the pack byte hash; refresh same-batch Viewer.
    update_viewer()
    return result


def complete_run(runner, authoring_result, *, pack=None, godot=None, dotnet=None, deps=None):
    from .offline_pipeline import convert_bundle
    stamp=datetime.now().strftime('%Y%m%d_%H%M%S')
    output=runner.output/'full-pipeline'
    if output.exists():raise ValueError('Full pipeline output exists; preserve evidence and choose a fresh run output')
    output.mkdir()
    receipt_path=output/'receipts.json';write_json(receipt_path,runner.results)
    assets=[dict(node=k,identity=r['identity'],outputs=r['outputs']) for k,r in runner.results.items() if r['op']=='deliver']
    bundle_path=output/'authoring-bundle.json'
    write_json(bundle_path,dict(assets=assets,productionReady=False,status=authoring_result['status']))
    deps=Path(deps or ROOT/'artifacts/kimodo_als/offline-python-deps')
    sys.path.insert(0,str(deps));sys.path.insert(0,str(runner.repo))
    pack=Path(pack or ROOT/'assets/generated'/('als_nonprop_'+stamp))
    bid=(authoring_result.get('viewerBatch') or {}).get('id')
    phase='conversion'
    try:
        conversion=convert_bundle(bundle_path,receipt_path,output/'conversion',pack,batch_id=bid)
        write_json(pack/'pipeline-provenance.json',dict(batchId=bid,authoringBundle=str(bundle_path),pipeline='pure-python-full-v1'))
        phase='runtime'
        runtime=verify_runtime(pack/'resource-pack.json',output/'runtime',godot,dotnet,runner.python)
    except Exception as error:
        result=dict(authoring=authoring_result,status='pipeline_complete_rejected',activated=False,
            failedPhase=phase,error=str(error),evidence=str(output),
            remainingStageStatus='blocked by '+phase+' failure')
        write_json(output/'result.json',result)
        return result
    accepted=(authoring_result['status']=='authoring_complete' and runtime['status']=='runtime_passed' and
        conversion['sourceClosureComplete'] and not conversion['failedTargetGaits'] and conversion['finalFbxReadback']['passed'])
    result=dict(authoring=authoring_result,conversion=conversion,runtime=runtime,
        status='pipeline_complete' if accepted else 'pipeline_complete_rejected',activated=False)
    write_json(output/'result.json',result)
    return result

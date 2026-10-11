"""Default production orchestration: delivered sources -> assets -> Godot checks."""
import os
import sys
import subprocess
from pathlib import Path
from datetime import datetime
from .graph import read_json
from .runner import write_json, file_hash
from .publish import ROOT, finalize_requests, validate_mantle_curve_delivery


def verify_runtime(pack, output, godot, dotnet, python, *, runtime_cases=None):
    """Keep fresh logs and attempt the matrix even if diagnostic acceptance fails."""
    output=Path(output).resolve();output.mkdir(parents=True,exist_ok=True)
    report=output/'runtime-result.json'
    if report.exists():raise ValueError('Runtime evidence already exists; use a fresh output directory')
    selected=read_json(pack)
    if selected.get('targetAirChecks') or selected.get('airPipelineContract'):
        try:
            from .air import validate_pack as validate_air_pack
            assessment=validate_air_pack(pack,selected)
        except (ValueError, KeyError, TypeError, OSError) as error:
            write_json(report,dict(status='runtime_failed',pack=str(pack),productionReady=False,activated=False,
                checks=[dict(name='air-pipeline-contract',passed=False,reason=str(error))]))
            raise ValueError('Air pipeline preflight failed; evidence: '+str(report)) from error
        write_json(output/'air-pipeline-contract.json',assessment)
    if selected.get('targetMantleChecks') or selected.get('mantleCurveContract'):
        try:
            assessment=validate_mantle_curve_delivery(pack,selected)
        except (ValueError, KeyError, TypeError) as error:
            write_json(report,dict(status='runtime_failed',pack=str(pack),productionReady=False,activated=False,
                checks=[dict(name='mantle-curve-contract',passed=False,reason=str(error))]))
            raise ValueError('Mantle curve contract preflight failed; evidence: '+str(report)) from error
        write_json(output/'mantle-curve-contract.json',assessment)
    resource='res://'+Path(pack).resolve().relative_to(ROOT).as_posix()
    env=os.environ.copy()
    for key in ('ALS_ASSET_ROOT','ALS_RAW_SOURCE_GENERATED_DIR','ALS_REQUIRE_KIMODO_SOURCE'):
        env.pop(key,None)
    env['ALS_RESOURCE_PACK_PATH']=resource
    checks=[]
    if selected.get('targetAirChecks') or selected.get('airPipelineContract'):
        checks.append(dict(name='air-pipeline-contract',passed=True,report=str(output/'air-pipeline-contract.json')))
    if selected.get('targetMantleChecks') or selected.get('mantleCurveContract'):
        checks.append(dict(name='mantle-curve-contract',passed=True,report=str(output/'mantle-curve-contract.json')))
    def update_viewer():
        from .viewer_pack import export_pack
        provenance=Path(pack).parent/'pipeline-provenance.json'
        config=read_json(provenance) if provenance.exists() else {}
        bid=config.get('batchId')
        if config.get('authoringBatchId') and config['authoringBatchId']!=bid:
            # A combined target pack contains several upstream authoring runs.
            # Register it independently instead of merging into one source run.
            bid=None
        return export_pack(Path(pack),batch_id=bid,make_latest=True)
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
    scoped=run('resource-scan',[python,ROOT/'tools/godot_resource_scan.py',
        '--apply','--include',resource,'--report',output/'resource-scan.json'])
    imported=scoped and run('godot-import',[godot,'--headless','--path',ROOT,'--editor','--import'])
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
        for case in runtime_cases or ():
            matrix_command += ['--only',case]
        if read_json(pack).get('missingNonPropSources'):matrix_command.append('--diagnostic-partial-pack')
        run('e2e-matrix',matrix_command,marker='ALS_NON_PROP_E2E_OK',timeout=7200)
        if selected.get('targetAirChecks'):
            run('air-scene',[python,ROOT/'scripts/verify-kimodo-air-scene.py',
                '--pack',pack,'--output',output/'air-scene','--godot',godot],timeout=2400)
        if read_json(pack).get('targetMantleChecks'):
            # Gameplay completion alone does not certify hand contact or skin.
            # Retain failures and still execute the independent platform cases.
            run('mantle-contact-static',[python,ROOT/'scripts/verify-kimodo-mantle-scene.py',
                '--pack',resource,'--output',output/'mantle-contact-static','--godot',godot],
                marker='KIMODO_MANTLE_SCENE_OK',timeout=2400)
            run('mantle-contact-platforms',[python,ROOT/'scripts/verify-mantle-contact-cases.py',
                '--pack',resource,'--output',output/'mantle-contact-platforms','--godot',godot],timeout=2400)
    else:
        checks.append(dict(name='e2e-matrix',passed=False,status='blocked',reason='Godot build/import failed'))
    result=dict(status='runtime_passed' if all(c['passed'] for c in checks) else 'runtime_failed',
        pack=resource,packSha256=file_hash(pack),checks=checks,productionReady=False,activated=False,
        requestedRuntimeCases=list(runtime_cases) if runtime_cases is not None else None)
    write_json(report,result)
    # Request finalization changes the pack byte hash; refresh same-batch Viewer.
    update_viewer()
    return result


def complete_run(runner, authoring_result, *, pack=None, godot=None, dotnet=None, deps=None, runtime_cases=None, base_pack=None):
    from .offline_pipeline import convert_bundle
    stamp=datetime.now().strftime('%Y%m%d_%H%M%S')
    pipeline_scope=getattr(runner,'graph',{}).get('pipeline','non-prop')
    output=runner.output/'full-pipeline'
    if output.exists():raise ValueError('Full pipeline output exists; preserve evidence and choose a fresh run output')
    output.mkdir()
    receipt_path=output/'receipts.json';write_json(receipt_path,runner.results)
    assets=[dict(node=k,identity=r['identity'],outputs=r['outputs']) for k,r in runner.results.items() if r['op']=='deliver']
    new_deliveries=len(assets)
    bundle_path=output/'authoring-bundle.json'
    write_json(bundle_path,dict(assets=assets,productionReady=False,status=authoring_result['status']))
    if not assets:
        # With no accepted generated sources, a native-only fallback pack
        # cannot supply evidence about the failed generation. Keep the source
        # receipts and explicitly block export/runtime instead of reporting
        # vacuous zero-asset FBX success and running native fallback scenes.
        result=dict(authoring=authoring_result,status='pipeline_complete_rejected',
            activated=False,productionReady=False,failedPhase='source-quality',
            sourceDeliveries=0,evidence=str(output),
            remainingStageStatus='blocked: no accepted generated source deliveries')
        write_json(output/'result.json',result)
        return result
    deps=Path(deps or ROOT/'artifacts/kimodo_als/offline-python-deps')
    sys.path.insert(0,str(deps));sys.path.insert(0,str(runner.repo))
    pack=Path(pack or ROOT/'assets/generated'/('als_nonprop_'+('air_' if pipeline_scope=='air' else '')+stamp))
    bid=(authoring_result.get('viewerBatch') or {}).get('id')
    phase='conversion'
    try:
        if base_pack:
            phase='base-merge'
            from .derived import retain_base_deliveries
            assets,combined_receipts=retain_base_deliveries(base_pack,assets,dict(runner.results))
            write_json(receipt_path,combined_receipts)
            write_json(bundle_path,dict(assets=assets,productionReady=False,status=authoring_result['status'],
                newSourceDeliveries=new_deliveries,basePack=str(Path(base_pack).resolve())))
        phase='conversion'
        conversion=convert_bundle(bundle_path,receipt_path,output/'conversion',pack,batch_id=None if base_pack else bid)
        target_bid=conversion['viewer']['id'] if base_pack else bid
        write_json(pack/'pipeline-provenance.json',dict(batchId=None if base_pack else target_bid,
            targetViewerBatchId=target_bid if base_pack else None,authoringBatchId=bid,
            authoringBundle=str(bundle_path),pipeline='pure-python-full-v1',productionLane=pipeline_scope))
        phase='runtime'
        runtime=verify_runtime(pack/'resource-pack.json',output/'runtime',godot,dotnet,runner.python,
            runtime_cases=runtime_cases)
    except Exception as error:
        result=dict(authoring=authoring_result,status='pipeline_complete_rejected',activated=False,
            failedPhase=phase,error=str(error),evidence=str(output),
            remainingStageStatus='blocked by '+phase+' failure')
        write_json(output/'result.json',result)
        return result
    accepted=(authoring_result['status']=='authoring_complete' and runtime['status']=='runtime_passed' and
        conversion['sourceClosureComplete'] and not conversion['failedTargetGaits'] and
        not conversion['failedTargetMantles'] and not conversion.get('failedTargetAir',[]) and conversion['finalFbxReadback']['passed'])
    result=dict(authoring=authoring_result,conversion=conversion,runtime=runtime,
        status='pipeline_complete' if accepted else 'pipeline_complete_rejected',activated=False,
        newSourceDeliveries=new_deliveries,retainedSourceDeliveries=len(assets)-new_deliveries)
    if pipeline_scope=='air':
        expected={k for k,n in runner.graph['nodes'].items() if n['op']=='deliver'}
        result.update(productionLane='air',sourceScopeComplete={a['node'] for a in assets}==expected,
                      sourceScopeAssets=len(expected),productionReady=False)
    write_json(output/'result.json',result)
    return result

"""Air lane semantics, bounded repairs, periodic FK and rejected old assets."""
import copy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import numpy as np
from scipy.spatial.transform import Rotation

from . import air
from .graph import read_json, load_rules, build_graph, topological_order
from .motion import complete, load_motion, save_motion
from .offline_retarget import convert
from .root_ownership import decompose
from .runner import Runner, file_hash, write_json
from .offline_fbx import write_fbx, readback

ROOT=Path(__file__).resolve().parents[2]


def source_rig():
    r=read_json(ROOT/'artifacts/kimodo_als/idle-arms-20261009-v2/receipts.json')
    return read_json(r['rig:soma']['outputs']['rig.json']['path'])


def candidate_fixture(rig, name):
    reference,_=air.native_reference(name,rig);period=len(reference['root_positions'])-1
    indices=np.arange(3*period+1)%period
    local=reference['local_rot_mats'][indices].copy();root=reference['root_positions'][indices].copy()
    # Deliberately distinct, bounded generated-like perturbation. This is a
    # controlled algorithm fixture, never claimed as Kimodo diffusion output.
    phase=np.arange(len(indices))/period*2*np.pi
    chest=rig['jointNames'].index('Chest')
    local[:,chest]=local[:,chest]@Rotation.from_euler('y',np.sin(phase)[:,None]*.02).as_matrix()
    root[:,1]+=.003*np.sin(phase)
    return reference,complete(local,root,rig,float(reference['fps']))


class AirTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.rig=source_rig()

    def test_independent_lane_has_no_planar_or_mantle_prerequisites(self):
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        graph=air.build_graph(contracts,load_rules(ROOT/'tools/kimodo_pipeline/air_rules.json'))
        self.assertEqual(graph['summary']['assets'],16)
        self.assertEqual(graph['summary']['operations']['generate'],3)
        self.assertEqual(graph['summary']['operations']['air_cycle'],3)
        self.assertFalse(any(n['op'] in ('loop','gait_cycle','mantle','native_gait_reference') for n in graph['nodes'].values()))
        self.assertFalse(any('Walk' in n or 'Mantle' in n or 'Stand_Pose' in n for n in graph['nodes']))
        for name in air.SEMANTICS:
            self.assertIn('reference:'+name+':native-air',graph['nodes']['candidate:'+name]['deps'])
        full=build_graph(contracts,load_rules(ROOT/'tools/kimodo_pipeline/directional_gait_rules.json'))
        self.assertEqual(full['nodes']['motion:Fall']['op'],'air_cycle')
        self.assertEqual(full['nodes']['motion:Mantle_High']['op'],'mantle')
        self.assertEqual(full['nodes']['motion:Walk_Forward']['op'],'gait_cycle')

    def test_fullbody_and_hips_conditions_include_vertical_and_no_ground_contacts(self):
        for name in air.SEMANTICS:
            reference,_=air.native_reference(name,self.rig)
            conditions,report=air.materialize_constraints(dict(semantic=name,airPolicy=air.POLICY,seconds=3,fps=30),reference,self.rig)
            self.assertEqual({c['type'] for c in conditions},{'fullbody','end-effector'})
            self.assertFalse(report['groundFootLock'])
            self.assertLessEqual(len(report['fullBodyFrames']),19)
            hips=next(c for c in conditions if c['type']=='end-effector')
            self.assertEqual(hips['joint_names'],['Hips'])
            np.testing.assert_allclose(hips['root_positions'],conditions[0]['root_positions'])
            self.assertGreater(np.asarray(hips['root_positions'])[:,1].min(),.7)

    def test_conditions_parse_with_real_kimodo_sdk(self):
        import sys
        sdk_root=Path(next(iter(self.rig['sourceAssets']))).parents[4]
        sys.path.insert(0,str(sdk_root))
        from kimodo.skeleton import SOMASkeleton77, SOMASkeleton30
        from kimodo.constraints import load_constraints_lst
        for skeleton in (SOMASkeleton77(),SOMASkeleton30()):
            for name in air.SEMANTICS:
                with self.subTest(skeleton=type(skeleton).__name__,semantic=name):
                    reference,_=air.native_reference(name,self.rig)
                    conditions,_=air.materialize_constraints(dict(semantic=name,airPolicy=air.POLICY,seconds=3,fps=30),reference,self.rig)
                    with tempfile.TemporaryDirectory() as d:
                        path=Path(d)/'conditions.json';write_json(path,conditions)
                        parsed=load_constraints_lst(str(path),skeleton)
                        self.assertEqual(len(parsed),2)
                        self.assertEqual(parsed[1].joint_names,['Hips'])
                        np.testing.assert_allclose(parsed[1].root_y_pos.cpu().numpy(),np.asarray(conditions[1]['root_positions'])[:,1],atol=1e-6)

    def test_bounded_source_to_target_cycles_and_real_fbx_readback(self):
        for name in air.SEMANTICS:
            with self.subTest(name=name):
                reference,candidate=candidate_fixture(self.rig,name)
                original=copy.deepcopy(candidate)
                motion,report,trajectory=air.author(candidate,reference,self.rig,name)
                self.assertTrue(report['passed'])
                self.assertEqual(trajectory['actorTrajectoryOwner'],'character-physics')
                np.testing.assert_array_equal(candidate['root_positions'],original['root_positions'])
                np.testing.assert_array_equal(candidate['local_rot_mats'],original['local_rot_mats'])
                native,skel,_=air.reference_input(name)
                timing={k:native[k] for k in ('playLength','sampledKeyCount','frameRateNumerator','frameRateDenominator')}
                raw,_=convert(motion,self.rig,skel,native['source'],timing)
                authored,runtime,ownership=decompose(raw,skel,family='flail' if name=='Flail' else 'fall')
                self.assertEqual(ownership['method'],'periodic-air-constant-origin-preserve-XYZ-residual')
                gate=air.analyze_target(runtime,skel,name)
                self.assertTrue(gate['passed'],gate)
                from .publish import world_pose
                np.testing.assert_array_equal(world_pose(raw,skel)['pelvis'][1][:,2],world_pose(runtime,skel)['pelvis'][1][:,2])
                with tempfile.TemporaryDirectory() as d:
                    fbx=write_fbx(Path(d)/'air.fbx',runtime,skel)
                    self.assertTrue(readback(fbx,runtime,skel)['passed'])

    def test_lying_down_and_large_vertical_fall_rejected_before_repairs(self):
        reference,candidate=candidate_fixture(self.rig,'Fall')
        for kind in ('lying','falling'):
            bad=copy.deepcopy(candidate)
            if kind=='lying':bad['local_rot_mats'][:,0]=Rotation.from_euler('x',np.full((len(bad['root_positions']),1),np.pi/2)).as_matrix()@bad['local_rot_mats'][:,0]
            else:bad['root_positions'][:,1]-=np.linspace(0,2,len(bad['root_positions']))
            with self.subTest(kind=kind),self.assertRaises(air.AirError) as ctx:
                air.author(bad,reference,self.rig,'Fall')
            self.assertFalse(ctx.exception.receipt['passed'])
            self.assertTrue(ctx.exception.receipt['attempts'])

    def test_current_three_wrong_assets_rejected_and_target_evidence_retained(self):
        receipts=read_json(ROOT/'artifacts/kimodo_als/idle-arms-20261009-v2/receipts.json')
        # Historical failing fixture stays fixed when the project pointer moves.
        pack=ROOT/'assets/generated/als_nonprop_20261010_mantle_high_combined_v5/resource-pack.json'
        jobs=read_json(read_json(pack)['nativeRetargetBundle'])['jobs']
        for name in air.SEMANTICS:
            source=load_motion(receipts['candidate:'+name]['outputs']['motion.npz']['path'],self.rig)
            reference,_=air.native_reference(name,self.rig)
            with self.subTest(name=name),self.assertRaises(air.AirError):air.author(source,reference,self.rig,name)
            job=next(j for j in jobs if j['edition']=='refactored' and j['contract']['semanticName']==name)
            import hashlib
            raw=read_json(pack.parent/'root-ownership'/hashlib.sha256(job['source'].encode()).hexdigest()/'runtime.raw.json')
            _,skel,_=air.reference_input(name)
            report=air.analyze_target(raw,skel,name)
            self.assertFalse(report['passed'])
            self.assertTrue(any(not c['passed'] for c in report['checks']))

    def test_target_detects_velocity_seam_even_with_identical_endpoints(self):
        raw,skel,_=air.reference_input('Fall');bad=copy.deepcopy(raw)
        track=next(t for t in bad['tracks'] if t['bone'].lower()=='pelvis')
        track['positions'][1][0]+=4
        report=air.analyze_target(bad,skel,'Fall')
        endpoint=next(c for c in report['checks'] if c['check']=='endpointPositionCm')
        velocity=next(c for c in report['checks'] if c['check']=='endpointVelocityMismatchCmPerSecond')
        self.assertTrue(endpoint['passed']);self.assertFalse(velocity['passed'])

    def test_missing_authoring_receipt_cannot_enter_diagnostic_stage(self):
        from .publish import prepare_target_raw
        raw,skel,_=air.reference_input('Fall')
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'raw.json';write_json(path,raw)
            row=next(r for r in read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')['assets'] if r['asset']=='A_Als_Fall')
            job=dict(source=raw['source'],edition='refactored',contract=row,raw=str(path),_diagnosticConversion=True)
            with self.assertRaisesRegex(ValueError,'dedicated policy|authoring receipt'):
                prepare_target_raw(job,skel,skel,read_json(ROOT/'assets/config/refactored_animation_sources.json'))

    def test_runtime_preflight_retains_failure_and_does_not_launch_godot(self):
        from .full_pipeline import verify_runtime
        with tempfile.TemporaryDirectory() as d:
            folder=Path(d);pack=folder/'pack.json'
            write_json(pack,dict(targetAirChecks=[dict(source='bad-air')]))
            with patch('tools.kimodo_pipeline.full_pipeline.subprocess.run') as process:
                with self.assertRaisesRegex(ValueError,'Air pipeline preflight failed'):
                    verify_runtime(pack,folder/'runtime','godot','dotnet','python')
            process.assert_not_called()
            self.assertEqual(read_json(folder/'runtime/runtime-result.json')['checks'][0]['name'],'air-pipeline-contract')

    def test_runner_delivers_all_16_identities_with_bound_fixture_provenance_and_strict_receipts(self):
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        graph=air.build_graph(contracts,load_rules(ROOT/'tools/kimodo_pipeline/air_rules.json'))
        with tempfile.TemporaryDirectory() as d:
            folder=Path(d);write_json(folder/'rig.json',self.rig)
            bindings={'rig:soma':dict(path=str(folder/'rig.json'))}
            for name in air.SEMANTICS:
                _,candidate=candidate_fixture(self.rig,name)
                source=folder/(name+'.npz');save_motion(source,candidate,self.rig)
                bindings['candidate:'+name]=dict(path=str(source))
            runner=Runner(graph,folder/'cache',bindings=bindings)
            # Viewer publication is unrelated to this temporary controlled fixture.
            with patch('tools.kimodo_pipeline.viewer_export.export_batch',return_value={}):
                result=runner.run(air.targets(contracts))
            self.assertEqual(result['status'],'authoring_complete')
            self.assertEqual(len(result['targets']),16)
            for key,receipt in result['targets'].items():
                row=graph['nodes'][key]['params']['contract']
                metadata=read_json(receipt['outputs']['asset.json']['path'])
                if row['family'] not in ('fall','flail'):continue
                motion=receipt['outputs']['motion.npz']
                job=dict(contract=row,targetAirPolicy=air.POLICY,motionPath=motion['path'],motionSha256=motion['sha256'],
                         airValidationReceipt=metadata['airValidationReceipt'],airTrajectory=metadata['airTrajectory'])
                self.assertTrue(air.validate_source_receipt(job)['passed'])
                # Rehashing a changed NPZ cannot bypass the certified array identity.
                if row['asset']=='A_Als_Fall':
                    altered=load_motion(motion['path'],self.rig);altered['root_positions'][3,1]+=.001
                    save_motion(motion['path'],altered,self.rig)
                    job['motionSha256']=file_hash(motion['path'])
                    job['airValidationReceipt']['deliveredMotionSha256']=job['motionSha256']
                    with self.assertRaisesRegex(ValueError,'motion identity differs'):air.validate_source_receipt(job)

    def test_actual_pack_preflight_accepts_valid_closure_and_rejects_forged_pass(self):
        from .publish import prepare_target_closure
        from .root_ownership import semantic_hash
        import hashlib
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        graph=air.build_graph(contracts,load_rules(ROOT/'tools/kimodo_pipeline/air_rules.json'))
        target='asset:refactored:A_Als_Fall'
        with tempfile.TemporaryDirectory(dir=ROOT/'artifacts',prefix='air-test-') as d:
            folder=Path(d);write_json(folder/'rig.json',self.rig)
            _,candidate=candidate_fixture(self.rig,'Fall');save_motion(folder/'candidate.npz',candidate,self.rig)
            runner=Runner(graph,folder/'cache',bindings={'rig:soma':dict(path=str(folder/'rig.json')),
                'candidate:Fall':dict(path=str(folder/'candidate.npz'))})
            with patch('tools.kimodo_pipeline.viewer_export.export_batch',return_value={}):runner.run([target])
            output=runner.results[target]['outputs'];metadata=read_json(output['asset.json']['path'])
            row=graph['nodes'][target]['params']['contract'];native,skel,_=air.reference_input('Fall')
            motion=load_motion(output['motion.npz']['path'],self.rig)
            raw,_=convert(motion,self.rig,skel,native['source'],row['originalTiming'])
            rawfile=folder/'retarget.raw.json';write_json(rawfile,raw)
            job=dict(source=native['source'],edition='refactored',contract=row,raw=str(rawfile),rawSha256=file_hash(rawfile),
                motionPath=output['motion.npz']['path'],motionSha256=output['motion.npz']['sha256'],
                targetAirPolicy=air.POLICY,airValidationReceipt=metadata['airValidationReceipt'],airTrajectory=metadata['airTrajectory'])
            source=job['source'];jobs={source:job}
            rewritten=prepare_target_closure(jobs,skel,skel,read_json(ROOT/'assets/config/refactored_animation_sources.json'))
            ident=hashlib.sha256(source.encode()).hexdigest()
            reportfile=folder/'target-air'/ident/'quality.json';reportfile.parent.mkdir(parents=True)
            runtimefile=folder/'root-ownership'/ident/'runtime.raw.json';runtimefile.parent.mkdir(parents=True)
            write_json(reportfile,job['_targetAirReport']);write_json(runtimefile,rewritten[source])
            bundle=folder/'retarget-bundle.json';write_json(bundle,dict(jobs=[{k:v for k,v in job.items() if not k.startswith('_')}]))
            entries=[dict(path='res://'+p.relative_to(ROOT).as_posix(),sha256=file_hash(p)) for p in (reportfile,runtimefile)]
            pack=dict(nativeRetargetBundle=str(bundle),airPipelineContract=air.contract(),files=entries,
                targetAirChecks=[dict(source=source,policy=air.POLICY,passed=True,file=reportfile.relative_to(folder).as_posix())])
            self.assertTrue(air.validate_pack(folder/'resource-pack.json',pack)['passed'])
            bad=copy.deepcopy(rewritten[source]);pelvis=next(t for t in bad['tracks'] if t['bone'].lower()=='pelvis')
            pelvis['positions']=(np.asarray(pelvis['positions'])+[0,0,100]).tolist();write_json(runtimefile,bad)
            forged=read_json(reportfile);forged['passed']=True;forged['finalRawSemanticSha256']=semantic_hash(bad);write_json(reportfile,forged)
            for entry,p in zip(pack['files'],(reportfile,runtimefile)):entry['sha256']=file_hash(p)
            with self.assertRaisesRegex(ValueError,'Air target gate failed'):air.validate_pack(folder/'resource-pack.json',pack)


if __name__=='__main__':unittest.main()

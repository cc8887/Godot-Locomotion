"""Offline conversion preserves axes/timing/bind and fails closed on quality."""
import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from types import SimpleNamespace
import numpy as np
from scipy.spatial.transform import Rotation
from .offline_fbx import write_fbx, readback, mesh_binding_skeleton
from .offline_retarget import convert, aligned_neutral, mapping, BASIS
from .publish import ROOT, stage, activate
from .motion import complete, fk


class OfflineFbxTests(unittest.TestCase):
    def setUp(self):
        self.skeleton=json.loads((ROOT/'assets/config/v4_recovery_movement_source_inputs.json').read_text(encoding='utf8'))['skeletons'][0]

    def test_all_physical_tracks_bind_timeline_and_independent_reader(self):
        raw=dict(sampledKeyCount=5,frameRateNumerator=30,frameRateDenominator=1,playLength=4/30,tracks=[])
        for i,(name,ref) in enumerate(zip(self.skeleton['rawBoneNames'],self.skeleton['referencePose'])):
            delta=Rotation.from_rotvec(np.arange(5)[:,None]*np.array([.007,.01,.003])*(i+1)/68)
            q=(Rotation.from_quat(ref['rotation'])*delta).as_quat()
            p=np.tile(np.asarray(ref['position'],dtype=float),(5,1));p+=np.arange(5)[:,None]*np.array([.2,-.3,.07])
            raw['tracks'].append(dict(bone=name,positions=p.tolist(),rotations=q.tolist(),scales=[ref['scale']]))
        with tempfile.TemporaryDirectory() as directory:
            fbx=write_fbx(Path(directory)/'test.fbx',raw,self.skeleton)
            report=readback(fbx,raw,self.skeleton)
            self.assertTrue(report['passed'],report);self.assertEqual(68,report['physicalBones'])
            self.assertIn('AnimStack::Unreal Take',fbx.read_text(encoding='utf8'))
            bad=copy.deepcopy(raw);bad['tracks'][1]['positions'][0][0]+=1.
            self.assertFalse(readback(fbx,bad,self.skeleton)['passed'])
            bad=copy.deepcopy(raw);bad['playLength']+=.5
            with self.assertRaisesRegex(ValueError,'timeline'):write_fbx(fbx,bad,self.skeleton)
            bad=copy.deepcopy(raw);bad['tracks'].pop()
            with self.assertRaisesRegex(ValueError,'physical tracks'):write_fbx(fbx,bad,self.skeleton)

    def test_neutral_alignment_and_reflection_conjugation(self):
        # Use the actual source rig identity bound by the current batch.
        receipts=json.loads((ROOT/'artifacts/kimodo_als/nonprop-regeneration-20261009-v1/combined-receipts.json').read_text(encoding='utf8'))
        rig=json.loads(Path(receipts['rig:soma']['outputs']['rig.json']['path']).read_text(encoding='utf8'))
        count=3;local=np.tile(np.eye(3),(count,len(rig['jointNames']),1,1))
        local[:,0]=Rotation.from_euler('y',np.array([0.,.2,.4])[:,None]).as_matrix()
        motion=complete(local,np.array([[0.,1.,0.],[.2,1.,.3],[.4,1.,.6]]),rig,30)
        timing=dict(sampledKeyCount=count,frameRateNumerator=30,frameRateDenominator=1,playLength=2/30,interpolation='Linear')
        original=copy.deepcopy(self.skeleton)
        raw,report=convert(motion,rig,self.skeleton,'test',timing)
        pelvis=next(t for t in raw['tracks'] if t['bone'].lower()=='pelvis')
        # Independent unit sanity: a 1 m hip cannot become a 97 m target hip.
        self.assertGreater(pelvis['positions'][0][2],60.)
        self.assertLess(pelvis['positions'][0][2],130.)
        self.assertGreater(report['uniformScale'],.5)
        self.assertLess(report['uniformScale'],1.5)
        np.testing.assert_allclose(np.asarray(pelvis['positions']),motion['root_positions']@BASIS.T*100*report['uniformScale'])
        neutral=aligned_neutral(self.skeleton,rig,mapping())
        names={n.lower():i for i,n in enumerate(self.skeleton['rawBoneNames'])}
        _,source=fk(np.tile(np.eye(3),(1,len(rig['jointNames']),1,1)),np.zeros((1,3)),rig)
        source_names={n:i for i,n in enumerate(rig['jointNames'])}
        for suffix,side in [('l','Left'),('r','Right')]:
            for bone,child,a,b in [('upperarm','lowerarm','Arm','ForeArm'),('thigh','calf','Leg','Shin')]:
                v=neutral[names[bone+'_'+suffix]]@np.asarray(self.skeleton['referencePose'][names[child+'_'+suffix]]['position'])
                expected=BASIS@(source[0,source_names[side+b]]-source[0,source_names[side+a]])
                np.testing.assert_allclose(v/np.linalg.norm(v),expected/np.linalg.norm(expected),atol=1e-10)
        self.assertEqual(original,self.skeleton)
        self.assertTrue(np.allclose(np.linalg.det(Rotation.from_quat(pelvis['rotations']).as_matrix()),1.))

    def test_diagnostic_pack_cannot_activate(self):
        with tempfile.TemporaryDirectory() as directory:
            pack=Path(directory)/'pack.json';verification=Path(directory)/'verification.json'
            pack.write_text('{"status":"diagnostic"}');verification.write_text('{}')
            with self.assertRaisesRegex(ValueError,'not eligible'):activate(pack,verification)

    def test_case_sensitive_fbx_names_follow_target_mesh_not_raw_fnames(self):
        mesh=ROOT/'assets/generated/als_v4/meshes/skeletal/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.fbx'
        export=mesh_binding_skeleton(self.skeleton,mesh)
        self.assertEqual('Pelvis',self.skeleton['rawBoneNames'][1])
        self.assertEqual('pelvis',export['rawBoneNames'][1])
        self.assertEqual([n.lower() for n in self.skeleton['rawBoneNames']],export['rawBoneNames'])

    def test_exported_bind_and_axes_match_immutable_mesh_fbx(self):
        import ufbx
        mesh=ROOT/'assets/generated/als_v4/meshes/skeletal/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.fbx'
        export=mesh_binding_skeleton(self.skeleton,mesh)
        raw=dict(sampledKeyCount=2,frameRateNumerator=30,frameRateDenominator=1,playLength=1/30,
            tracks=[dict(bone=n,positions=[r['position']],rotations=[r['rotation']],scales=[r['scale']])
                    for n,r in zip(self.skeleton['rawBoneNames'],self.skeleton['referencePose'])])
        with tempfile.TemporaryDirectory() as directory:
            path=write_fbx(Path(directory)/'bind.fbx',raw,export)
            original=ufbx.load_file(str(mesh));written=ufbx.load_file(str(path))
            old_list=original.nodes;new_list=written.nodes
            old={n.name:n for n in old_list};new={n.name:n for n in new_list}
            old_settings=original.settings;new_settings=written.settings
            self.assertEqual(old_settings.axes,new_settings.axes)
            self.assertEqual(old_settings.unit_meters,new_settings.unit_meters)
            for name in export['rawBoneNames']:
                a=old[name].local_transform;b=new[name].local_transform
                np.testing.assert_allclose(a.translation,b.translation,atol=.01)
                error=(Rotation.from_quat(a.rotation).inv()*Rotation.from_quat(b.rotation)).magnitude()
                self.assertLess(error,np.deg2rad(.1),name)

    def test_incomplete_closure_still_fails_normal_stage(self):
        with tempfile.TemporaryDirectory() as directory:
            bundle=Path(directory)/'bundle.json';bundle.write_text('{"jobs":[]}')
            with self.assertRaisesRegex(ValueError,'Incomplete native retarget closure'):
                stage(bundle,ROOT/'assets/generated/als_nonprop_unittest_never_created')

    def test_zero_deliveries_cannot_validate_native_fallback_as_generated(self):
        from .full_pipeline import complete_run
        with tempfile.TemporaryDirectory() as directory:
            folder=Path(directory)
            runner=SimpleNamespace(output=folder/'run',repo=folder,python=sys.executable,
                results={'candidate:mantle':dict(op='generate',identity='rejected-source',outputs={})})
            runner.output.mkdir();pack=folder/'pack'
            with patch('tools.kimodo_pipeline.offline_pipeline.convert_bundle') as converted, \
                 patch('tools.kimodo_pipeline.full_pipeline.verify_runtime') as runtime:
                result=complete_run(runner,dict(status='authoring_rejected'),pack=pack)
            converted.assert_not_called();runtime.assert_not_called()
            self.assertEqual(result['sourceDeliveries'],0)
            self.assertEqual(result['failedPhase'],'source-quality')
            self.assertEqual(result['status'],'pipeline_complete_rejected')
            self.assertFalse(pack.exists())
            self.assertTrue((runner.output/'full-pipeline/authoring-bundle.json').is_file())

    def test_rejected_authoring_still_converts_and_runs_runtime(self):
        from .full_pipeline import complete_run
        with tempfile.TemporaryDirectory() as directory:
            folder=Path(directory)
            runner=SimpleNamespace(output=folder/'run',repo=folder,python=sys.executable,
                results={'asset:v4:walk':dict(op='deliver',identity='new-source',outputs={}),
                         'candidate:run':dict(op='generate',identity='failed-source',outputs={})})
            runner.output.mkdir()
            pack=folder/'pack'
            def conversion(bundle,receipts,output,pack,**kwargs):
                self.assertEqual(['asset:v4:walk'],[a['node'] for a in json.loads(bundle.read_text())['assets']])
                pack.mkdir()
                return dict(sourceClosureComplete=False,failedTargetGaits=[],finalFbxReadback=dict(passed=True))
            with patch('tools.kimodo_pipeline.offline_pipeline.convert_bundle',side_effect=conversion) as converted, \
                 patch('tools.kimodo_pipeline.full_pipeline.verify_runtime',return_value=dict(status='runtime_failed')) as runtime:
                result=complete_run(runner,dict(status='authoring_rejected'),pack=pack,godot='godot',dotnet='dotnet')
            self.assertEqual(1,converted.call_count);self.assertEqual(1,runtime.call_count)
            self.assertEqual('pipeline_complete_rejected',result['status']);self.assertFalse(result['activated'])

    def test_skin_rejection_blocks_runtime_acceptance_after_gameplay_pass_and_keeps_platform_check(self):
        from .full_pipeline import verify_runtime
        with tempfile.TemporaryDirectory(dir=ROOT/'artifacts') as directory:
            folder=Path(directory);pack=folder/'resource-pack.json'
            pack.write_text(json.dumps(dict(targetMantleChecks=[dict(source='generated-low')])))
            markers='P2B_IMPORT_OK RAW_SOURCE_REQUEST_OK ALS_NON_PROP_E2E_OK'
            commands=[]
            def process(command,**kwargs):
                commands.append(command)
                rejected=any('verify-kimodo-mantle-scene.py' in str(c) for c in command)
                kwargs['stdout'].write('KIMODO_MANTLE_SCENE_FAILED: published skin exceeds 2cm' if rejected else markers)
                return SimpleNamespace(returncode=1 if rejected else 0)
            with patch('tools.kimodo_pipeline.full_pipeline.subprocess.run',side_effect=process), \
                 patch('tools.kimodo_pipeline.full_pipeline.finalize_requests'), \
                 patch('tools.kimodo_pipeline.full_pipeline.validate_mantle_curve_delivery',
                       return_value=dict(passed=True,policy='mantle-curve-semantics-v1')) as contract_check, \
                 patch('tools.kimodo_pipeline.viewer_pack.export_pack',return_value={}):
                result=verify_runtime(pack,folder/'runtime','godot','dotnet',sys.executable)
            contract_check.assert_called_once()
            self.assertTrue(next(c for c in result['checks'] if c['name']=='mantle-curve-contract')['passed'])
            self.assertEqual('runtime_failed',result['status']);self.assertFalse(result['productionReady'])
            self.assertTrue(next(c for c in result['checks'] if c['name']=='e2e-matrix')['passed'])
            self.assertFalse(next(c for c in result['checks'] if c['name']=='mantle-contact-static')['passed'])
            self.assertTrue(any('verify-mantle-contact-cases.py' in str(c) for command in commands for c in command))


if __name__=='__main__':unittest.main()

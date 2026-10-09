"""Replacement scope, dependent pose endpoints and target root/IK regressions."""
import copy
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
import numpy as np
from .graph import build_graph,read_json,topological_order,target_origin_reference
from .scope import is_prop,non_prop_targets
from .publish import target_root_and_ik,world_pose,verify_compatible,activate,file_hash,write_json,curve_values,rewrite_curves,validate_gait_receipt,sample_native_track,prepare_target_raw,prepare_target_closure
from .motion import complete,upper_overlay,materialize_constraints
from scipy.spatial.transform import Rotation

ROOT=Path(__file__).resolve().parents[2]

class PublicationTests(unittest.TestCase):
    def test_pose_origin_closure_is_order_independent_and_retains_base_deltas(self):
        from .test_root_ownership import fixture,track
        master,skel=fixture();t=np.linspace(0,2*np.pi,31)
        track(master,'pelvis')['positions']=np.column_stack((2+np.sin(t),350+np.cos(t),90+np.sin(t))).tolist()
        base=copy.deepcopy(master)
        for item in base['tracks']:
            for key in ('positions','rotations','scales'):
                if len(item[key])>1:item[key]=[item[key][8]]
        variant=copy.deepcopy(base);track(variant,'pelvis')['positions'][0][0]+=1
        with tempfile.TemporaryDirectory(prefix='origin-closure-') as directory:
            motion=Path(directory)/'motion.npz'
            np.savez(motion,root_positions=np.zeros((31,3)),fps=30)
            def job(name,family,base_source=None):
                return dict(source=name,edition='v4',motionPath=str(motion),contract=dict(source=name,edition='v4',
                    semanticName=name,family=family,originalEvaluation=dict(enableRootMotion=False,baseAsset=base_source,baseFrame=0)))
            jobs={name:job(name,family,parent) for name,family,parent in
                  [('Acceleration_Forward','acceleration_additive','Lean_Pose'),('Lean_Pose','base_pose',None),
                   ('Run_Forward_Pose','gait_pose',None),('Run_Forward','gait_cycle',None)]}
            raws={'Run_Forward':master,'Run_Forward_Pose':base,'Lean_Pose':base,'Acceleration_Forward':variant}
            def adapt(j,*args,**kwargs):return copy.deepcopy(raws[j['source']]),skel
            with patch('tools.kimodo_pipeline.publish.prepare_target_raw',side_effect=adapt):
                result=prepare_target_closure(jobs,skel,skel,{})
                reversed_jobs=copy.deepcopy(dict(reversed(list(jobs.items()))))
                other=prepare_target_closure(reversed_jobs,skel,skel,{})
            self.assertEqual(result,other)
            for name in ('Lean_Pose','Run_Forward_Pose','Acceleration_Forward'):
                self.assertLess(jobs[name]['_rootOwnershipReport']['maximumRuntimePelvisRootHorizontalCm'],3)
            np.testing.assert_allclose(world_pose(result['Acceleration_Forward'],skel)['pelvis'][1]-
                world_pose(result['Lean_Pose'],skel)['pelvis'][1],np.tile([1,0,0],(31,1)),atol=1e-12)
            reference=jobs['Acceleration_Forward']['_rootOwnershipReport']['canonicalOriginReference']
            self.assertEqual(reference['source'],'Lean_Pose')
            self.assertEqual(reference['sourceRuntimeSemanticSha256'],jobs['Lean_Pose']['_rootOwnershipReport']['runtimeRawSemanticSha256'])

    def test_mirrored_gait_pose_uses_matching_master_and_missing_dependencies_fail(self):
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        for edition in ('v4','refactored'):
            pose=next(r for r in contracts['assets'] if r['edition']==edition and r['semanticName']=='Run_Right_Pose')
            spec=target_origin_reference(pose,contracts['assets'])
            parent=next(r for r in contracts['assets'] if r['source']==spec['source'])
            self.assertEqual(parent['semanticName'],'Run_Right_Forward')
            self.assertEqual(parent['edition'],edition)
            with self.assertRaisesRegex(ValueError,'Missing target origin gait master'):
                target_origin_reference(pose,[pose])

    def test_moving_additive_base_uses_its_sampled_trajectory_not_only_start_origin(self):
        from .test_root_ownership import fixture,track
        master,skel=fixture();track(master,'pelvis')['positions']=[[100+200*i/30,0,90] for i in range(31)]
        pose=copy.deepcopy(master);track(pose,'pelvis')['positions']=[[200,0,90]]
        with tempfile.TemporaryDirectory(prefix='moving-base-origin-') as directory:
            motion=Path(directory)/'motion.npz';np.savez(motion,root_positions=np.zeros((31,3)),fps=30)
            jobs={name:dict(source=name,edition='v4',motionPath=str(motion),contract=dict(source=name,edition='v4',
                semanticName=name,family=family,originalEvaluation=dict(enableRootMotion=False,baseAsset=base,baseFrame=15)))
                for name,family,base in [('Air_Lean','lean_additive','Fall'),('Fall','air',None)]}
            with patch('tools.kimodo_pipeline.publish.prepare_target_raw',side_effect=lambda j,*a,**k:
                       (copy.deepcopy(pose if j['source']=='Air_Lean' else master),skel)):
                result=prepare_target_closure(jobs,skel,skel,{})
            np.testing.assert_allclose(jobs['Air_Lean']['_rootOwnershipReport']['canonicalOriginTranslationCm'],[200,0,0])
            np.testing.assert_allclose(world_pose(result['Air_Lean'],skel)['pelvis'][1][0],
                                       world_pose(result['Fall'],skel)['pelvis'][1][15])

    def test_reference_fallback_does_not_author_evaluated_virtual_bones_as_fbx_tracks(self):
        from .test_root_ownership import fixture
        raw,skel=fixture(4);skel['source']='/Game/Target/Target_Skeleton.Target_Skeleton'
        raw['tracks'].append(dict(bone='VB hand',positions=[[80,0,0]],rotations=[[0,0,0,1]],scales=[[1,1,1]]))
        with tempfile.TemporaryDirectory(prefix='reference-raw-') as directory:
            native=Path(directory)/'native.json';write_json(native,raw)
            job=dict(source='/TestRootOwnership/Reference.Reference',edition='v4',raw=str(native),
                contract=dict(family='reference',originalEvaluation=dict(enableRootMotion=False),
                    originalTiming={k:raw[k] for k in ('sampledKeyCount','playLength','frameRateNumerator','frameRateDenominator')}))
            final,_=prepare_target_raw(job,skel,skel,{})
            self.assertEqual(set(skel['rawBoneNames']),{t['bone'] for t in final['tracks']})
            np.testing.assert_allclose(world_pose(final,skel)['pelvis'][1],world_pose(raw,skel)['pelvis'][1])

    def test_additive_lock_controls_are_not_replaced_by_positive_contacts(self):
        job={'contract':{'originalTiming':{'sampledKeyCount':4,'playLength':.1},
                         'originalEvaluation':{'additiveType':'AAT_RotationOffsetMeshSpace'}}}
        original={'curves':[{'name':'FootLeftLock','keys':[{'time':0.,'value':0.},
            {'time':.05,'value':-1.},{'time':.1,'value':0.}]},
            {'name':'FootRightLock','keys':[{'time':0.,'value':0.},{'time':.1,'value':1.}]}]}
        result=rewrite_curves(original,job,[np.ones(4),np.ones(4)])
        self.assertEqual(original,result)

    def test_native_extra_end_key_does_not_blend_pose_table_slots(self):
        native=dict(sampledKeyCount=5,frameRateNumerator=30,frameRateDenominator=1)
        target=dict(sampledKeyCount=4,frameRateNumerator=30,frameRateDenominator=1)
        positions=[[0,0,0],[0,1,0],[0,3,0],[0,3,0],[0,3,0]]
        np.testing.assert_array_equal(sample_native_track(positions,native,target),positions[:4])
        rotations=Rotation.from_euler('x',np.asarray([0,20,80,80,80])[:,None],degrees=True)
        got=Rotation.from_quat(sample_native_track(rotations.as_quat(),native,target,True))
        np.testing.assert_allclose((got*rotations[:4].inv()).magnitude(),0,atol=1e-12)

    def test_actual_time_resampling_handles_different_rates_and_rejects_missing_tail(self):
        native=dict(sampledKeyCount=5,frameRateNumerator=60,frameRateDenominator=1)
        target=dict(sampledKeyCount=3,frameRateNumerator=30,frameRateDenominator=1)
        values=[[v,0,0] for v in range(5)]
        np.testing.assert_array_equal(sample_native_track(values,native,target),[values[0],values[2],values[4]])
        target['sampledKeyCount']=4
        with self.assertRaisesRegex(ValueError,'does not cover'):
            sample_native_track(values,native,target)

    def test_failed_or_stale_authoring_receipt_cannot_enter_target_stage(self):
        with tempfile.TemporaryDirectory(prefix='gait-receipt-') as directory:
            folder=Path(directory);source=folder/'source.npz';delivered=folder/'motion.npz'
            source.write_bytes(b'source-motion');delivered.write_bytes(b'delivered-motion')
            receipt=folder/'gait-validation.json';trajectory=folder/'authoring-trajectory.json'
            write_json(receipt,{'pass':True});write_json(trajectory,{'kind':'removed_authoring_trajectory'})
            job=dict(source='pilot',motionPath=str(delivered),motionSha256=file_hash(delivered),
                gaitValidationReceipt=dict(file=str(receipt),sha256=file_hash(receipt),
                    sourceMotionFile=str(source),sourceMotionSha256=file_hash(source),deliveredMotionSha256=file_hash(delivered)),
                authoringTrajectory=dict(file=str(trajectory),sha256=file_hash(trajectory)))
            validate_gait_receipt(job)
            write_json(receipt,{'pass':False});job['gaitValidationReceipt']['sha256']=file_hash(receipt)
            with self.assertRaisesRegex(ValueError,'Upstream gait validation failed'):validate_gait_receipt(job)
            write_json(receipt,{'pass':True});job['gaitValidationReceipt']['sha256']=file_hash(receipt)
            delivered.write_bytes(b'changed-delivered-motion')
            with self.assertRaisesRegex(ValueError,'does not bind delivered motion'):validate_gait_receipt(job)
            delivered.write_bytes(b'delivered-motion');trajectory.write_bytes(b'changed-trajectory')
            with self.assertRaisesRegex(ValueError,'trajectory changed'):validate_gait_receipt(job)

    def test_gait_support_curve_uses_semantic_phases_without_adding_lock_curves(self):
        job={'contract':{'originalTiming':{'sampledKeyCount':4,'playLength':.1}}}
        contacts=[np.array([1,1,0,0]),np.array([0,0,1,1])]
        report={'footPlanted':[-.25,-1,.25,1]}
        source={'curves':[{'name':'FootPlanted','keys':[{'time':0,'value':0}]}]}
        result=rewrite_curves(source,job,contacts,report)
        self.assertEqual(['FootPlanted'],[c['name'] for c in result['curves']])
        self.assertEqual(report['footPlanted'],[k['value'] for k in result['curves'][0]['keys']])
        self.assertEqual([0],[k['value'] for k in source['curves'][0]['keys']])
        np.testing.assert_array_equal([-.2,-1,.2,1],curve_values('Feet_Position',job,contacts,report))

    def test_compatibility_envelope_does_not_reintroduce_reversed_half_amplitude(self):
        job={'contract':{'originalTiming':{'sampledKeyCount':2,'playLength':.1}}}
        contacts=[np.array([1.,0.]),np.array([0.,1.])]
        for name in ('FootPlanted','Feet_Position'):
            np.testing.assert_array_equal([-1.,1.],curve_values(name,job,contacts))

    def test_failed_skating_cannot_activate_otherwise_complete_runtime_matrix(self):
        pointer=ROOT/'assets/config/als_resource_pack.json'
        before=pointer.read_bytes()
        with tempfile.TemporaryDirectory(prefix='activation-gate-',dir=ROOT/'artifacts/tests') as directory:
            root=Path(directory);pack=root/'resource-pack.json';summary=root/'summary.json';evidence=root/'verification.json'
            write_json(pack,{'scope':'non-prop'})
            write_json(summary,dict(pack='res://'+pack.relative_to(ROOT).as_posix(),packSha256=file_hash(pack),
                completeMatrix=True,total=71,passed=71,failed=[],payloadChanges=[],animationSettingsChanged=False,
                singleParallelParity=[dict(hz=hz,passed=True) for hz in (30,60,120)],directionalQuality={'passed':False}))
            write_json(evidence,{'e2eSummary':str(summary)})
            with self.assertRaisesRegex(ValueError,'Directional skating quality failed'):
                activate(pack,evidence)
            self.assertEqual(before,pointer.read_bytes())

    def test_roll_path_moves_the_matching_endpoint_pose_forward(self):
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        rules=read_json(ROOT/'tools/kimodo_pipeline/non_prop_rules.json')
        node=build_graph(contracts,rules)['nodes']['candidate:Roll']['params']
        self.assertEqual('linear',node['rootPath']['kind'])
        rig=dict(jointNames=['Root'],parents=[-1],offsets=[[0,0,0]])
        base=complete(np.eye(3).reshape(1,1,3,3),np.array([[0,.99,0]]),rig)
        constraints=materialize_constraints(node,lambda _:base,rig)
        path=next(c for c in constraints if c['type']=='root2d')
        anchor=next(c for c in constraints if c['type']=='fullbody')
        self.assertGreater(path['smooth_root_2d'][-1][1],1)
        self.assertAlmostEqual(rules.get('rollTravelMeters',1.8),path['smooth_root_2d'][-1][1])
        np.testing.assert_allclose(np.asarray(anchor['root_positions'])[:,[0,2]],np.asarray(path['smooth_root_2d'])[anchor['frame_indices']])

    def test_non_prop_scope_is_complete_and_has_no_external_input(self):
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        rules=read_json(ROOT/'tools/kimodo_pipeline/non_prop_rules.json')
        graph=build_graph(contracts,rules);order=topological_order(graph['nodes'],non_prop_targets(contracts))
        self.assertEqual(183,len(non_prop_targets(contracts)))
        self.assertEqual(70,sum(is_prop(r) for r in contracts['assets']))
        self.assertFalse([key for key in order if graph['nodes'][key]['op']=='external'])
        for table in ('Default','Normal','Feminine','Masculine','HandsTied','Injured'):
            node=graph['nodes'][f'pose:{table}_Poses:3']
            self.assertEqual('copy',node['op']);self.assertEqual([f'pose:{table}_Poses:2'],node['deps'])

    def test_root_extraction_preserves_body_and_solves_helpers(self):
        names=['root','pelvis','head','foot_l','foot_r','hand_l','hand_r','ik_foot_root','ik_foot_l','ik_foot_r','ik_hand_root','ik_hand_gun','ik_hand_l','ik_hand_r']
        parents=[-1,0,1,1,1,1,1,0,7,7,0,10,11,11]
        transforms=[dict(position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1]) for _ in names]
        skel=dict(rawBoneNames=names,rawParents=parents,referencePose=transforms)
        raw=dict(sampledKeyCount=4,tracks=[dict(bone=n,positions=[[i*2,0,90] if n=='pelvis' else [0,0,0] for i in range(4)],rotations=[[0,0,0,1]]*4,scales=[[1,1,1]]) for n in names])
        before=world_pose(raw,skel)
        job={'contract':{'family':'roll','originalEvaluation':{'enableRootMotion':True}}}
        after_raw=target_root_and_ik(copy.deepcopy(raw),skel,job);after=world_pose(after_raw,skel)
        np.testing.assert_allclose(after['root'][1][:,0],[0,2,4,6],atol=1e-12)
        for name in ('pelvis','head','foot_l','foot_r','hand_l','hand_r'):np.testing.assert_allclose(after[name][1],before[name][1],atol=1e-12)
        for helper,body in (('ik_foot_l','foot_l'),('ik_foot_r','foot_r'),('ik_hand_l','hand_l'),('ik_hand_r','hand_r')):np.testing.assert_allclose(after[helper][1],after[body][1],atol=1e-12)

    def test_upper_style_cannot_stand_up_a_crouch(self):
        rig=dict(jointNames=['Root','Spine1','Chest','LeftLeg','LeftFoot'],parents=[-1,0,1,0,3],offsets=[[0,0,0],[0,1,0],[0,1,0],[1,0,0],[0,-1,0]])
        identity=np.broadcast_to(np.eye(3),(1,5,3,3)).copy()
        reference=complete(identity,np.array([[0,1,0]]),rig)
        crouch_local=identity.copy();crouch_local[0,3]=Rotation.from_euler('x',45,degrees=True).as_matrix()
        crouch=complete(crouch_local,np.array([[0,.75,0]]),rig)
        styled=identity.copy();styled[0,2]=Rotation.from_euler('z',10,degrees=True).as_matrix();styled[0,3]=Rotation.from_euler('y',30,degrees=True).as_matrix()
        result=upper_overlay(crouch,complete(styled,np.array([[0,.99,0]]),rig),reference,rig)
        np.testing.assert_array_equal(result['root_positions'],crouch['root_positions'])
        np.testing.assert_array_equal(result['local_rot_mats'][:,[0,3,4]],crouch['local_rot_mats'][:,[0,3,4]])
        np.testing.assert_allclose(result['posed_joints'][:,[3,4]],crouch['posed_joints'][:,[3,4]],atol=1e-12)
        np.testing.assert_allclose(result['local_rot_mats'][:,2],styled[:,2],atol=1e-12)

    def test_physical_bind_difference_requires_real_retarget(self):
        base=dict(rawBoneNames=['root'],rawParents=[-1],referencePose=[dict(position=[0,0,0],rotation=[0,0,0,1],scale=[1,1,1])])
        incompatible=copy.deepcopy(base);incompatible['referencePose'][0]['position'][0]=1
        with self.assertRaisesRegex(ValueError,'bind transform'):verify_compatible(base,incompatible)
        virtual=copy.deepcopy(base);virtual['referencePose'].append(dict(position=[5,0,0],rotation=[0,0,0,1],scale=[1,1,1]))
        verify_compatible(base,virtual)

if __name__=='__main__':unittest.main()

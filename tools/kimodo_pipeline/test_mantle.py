"""Real native/SOMA geometry, negative contacts and final delivery boundaries."""
import copy
import unittest
from unittest.mock import patch
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation

from .graph import read_json, load_rules, build_graph
from .mantle import (PROFILES, POLICY, reference_input, native_reference, target,
                     author, materialize_constraints, MantleError, contact_curves)
from .motion import complete
from .root_ownership import decompose
from .publish import world_pose
from .offline_retarget import convert

ROOT = Path(__file__).resolve().parents[2]


def soma_rig():
    # Installed source skeleton is the actual pipeline rig, not a toy arm.
    receipts = read_json(ROOT/'artifacts/kimodo_als/nonprop-regeneration-20261009-v1/combined-receipts.json')
    return read_json(receipts['rig:soma']['outputs']['rig.json']['path'])


class MantleTests(unittest.TestCase):
    def test_native_contacts_pass_and_low_support_side_is_explicit(self):
        self.assertEqual(PROFILES['Mantle_Low_Left']['hands'], ('right',))
        self.assertEqual(PROFILES['Mantle_Low_Right']['hands'], ('left',))
        for name in PROFILES:
            with self.subTest(name=name):
                raw, skel, _ = reference_input(name)
                corrected, path, rot, report = target(raw, skel, name)
                self.assertTrue(report['passed'], report)
                authored, runtime, receipt = decompose(corrected, skel, family='mantle',
                    mantle_root_positions=path, root_rotation=rot)
                before = world_pose(corrected, skel); after = world_pose(authored, skel)
                np.testing.assert_allclose(after['root'][1][-1], [0,65,PROFILES[name]['heightCm']], atol=1e-6)
                for bone in ('pelvis','hand_l','hand_r','foot_l','foot_r'):
                    np.testing.assert_allclose(before[bone][1], after[bone][1], atol=1e-4)
                self.assertGreater(np.ptp(next(t for t in authored['tracks'] if t['bone']=='pelvis')['positions'],axis=0)[2], 50)
                self.assertEqual(runtime, authored)

    def test_large_body_offset_is_rejected_and_not_teleported(self):
        raw, skel, _ = reference_input('Mantle_High'); bad = copy.deepcopy(raw)
        pelvis = next(t for t in bad['tracks'] if t['bone']=='pelvis')
        pelvis['positions'] = (np.asarray(pelvis['positions'])+[0,0,100]).tolist()
        result, _, _, report = target(bad, skel, 'Mantle_High')
        self.assertFalse(report['passed'])
        self.assertTrue(all(not r['applied'] for r in report['repairs']))
        np.testing.assert_allclose(next(t for t in result['tracks'] if t['bone']=='pelvis')['positions'],pelvis['positions'])

    def test_target_mesh_adaptation_is_fixed_bounded_and_gated_against_original(self):
        raw,skel,_=reference_input('Mantle_High')
        corrected,_,_,report=target(raw,skel,'Mantle_High')
        self.assertTrue(report['passed'])
        before=world_pose(raw,skel);after=world_pose(corrected,skel)
        np.testing.assert_allclose(after['hand_l'][1][18:59]-before['hand_l'][1][18:59],
            np.broadcast_to([0,0,1],(41,3)),atol=1e-5)
        self.assertAlmostEqual(next(c['value'] for c in report['checks'] if c['check']=='leftHandPositionCm'),1.,places=5)
        with patch.dict(PROFILES['Mantle_High'],targetMeshHandOffsetsCm={'left':(0,0,3)}):
            with self.assertRaisesRegex(ValueError,'original position tolerance'):target(raw,skel,'Mantle_High')

    def test_palm_orientation_is_a_gate_not_only_wrist_position(self):
        raw, skel, _ = reference_input('Mantle_Low_Left'); bad = copy.deepcopy(raw)
        wrist = next(t for t in bad['tracks'] if t['bone']=='hand_r')
        wrist['rotations'] = (Rotation.from_quat(wrist['rotations'])*Rotation.from_euler('x',90,degrees=True)).as_quat().tolist()
        _, _, _, report = target(bad, skel, 'Mantle_Low_Left')
        self.assertFalse(report['passed'])
        check = next(c for c in report['checks'] if c['check']=='rightHandOrientationDegrees')
        self.assertGreater(check['value'],80)

    def test_bounded_target_ik_repairs_a_small_hand_offset_without_stretch(self):
        raw, skel, _ = reference_input('Mantle_Low_Left'); bad = copy.deepcopy(raw)
        arm = next(t for t in bad['tracks'] if t['bone']=='upperarm_r')
        arm['rotations'] = (Rotation.from_quat(arm['rotations'])*Rotation.from_euler('y',2,degrees=True)).as_quat().tolist()
        result, _, _, report = target(bad, skel, 'Mantle_Low_Left')
        self.assertTrue(report['passed'], report)
        self.assertTrue(report['repairs'][0]['applied'])
        for old, new in zip(bad['tracks'],result['tracks']):
            self.assertEqual(old['positions'],new['positions'])
            self.assertEqual(old['scales'],new['scales'])

    def test_source_constraints_include_palm_rotation_and_contact_frames(self):
        rig = soma_rig()
        for name in PROFILES:
            guide, receipt = native_reference(name, rig)
            conditions, contract = materialize_constraints(dict(semantic=name,seconds=3,fps=30),guide,rig)
            kinds = {c['type']:c for c in conditions}
            self.assertEqual(set(kinds), {'fullbody','end-effector','left-foot','right-foot'}|{s+'-hand' for s in PROFILES[name]['hands']})
            self.assertLessEqual(max(np.diff(kinds['fullbody']['frame_indices'])),7)
            self.assertTrue(all(len(c['frame_indices']) <= 19 for c in conditions))
            if name == 'Mantle_High':
                end = reference_input(name)[0]['playLength']
                self.assertIn(round((73/30)/end*89), kinds['fullbody']['frame_indices'])
            for side in PROFILES[name]['hands']:
                hand = kinds[side+'-hand']
                self.assertGreater(len(hand['frame_indices']),2)
                self.assertEqual(len(hand['local_joints_rot']),len(hand['root_positions']))
                self.assertLessEqual(len(hand['frame_indices']),19)
            self.assertFalse(receipt['deliverable'])
            valid, gate = author(guide,guide,rig,name)
            self.assertTrue(gate['passed'])
            self.assertEqual(len(valid['root_positions']),contract['deliveryFrames'])
            bad = complete(guide['local_rot_mats'],guide['root_positions']+[0,.5,0],rig,float(guide['fps']))
            with self.assertRaises(MantleError) as rejected:
                author(bad,guide,rig,name)
            self.assertFalse(rejected.exception.receipt['passed'])

    def test_inverse_source_guide_can_retarget_and_keep_independent_target_gate(self):
        rig = soma_rig()
        for name in PROFILES:
            raw,skel,_=reference_input(name);guide,_=native_reference(name,rig)
            timing={k:raw[k] for k in ('sampledKeyCount','playLength','frameRateNumerator','frameRateDenominator')}
            converted,_=convert(guide,rig,skel,raw['source'],timing)
            _,path,_,report=target(converted,skel,name)
            # Conditioning is not a target-quality certificate. Record the
            # independent gate and ensure root is not the source hip track.
            self.assertEqual(report['policy'],POLICY)
            self.assertTrue(report['passed'],report)
            np.testing.assert_allclose(path[-1],[0,65,PROFILES[name]['heightCm']],atol=1e-6)
            self.assertTrue(np.isfinite(path).all())

    def test_mantle_graph_has_own_constraints_gate_and_no_gait_mirror(self):
        contracts=read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json')
        rules=load_rules(ROOT/'tools/kimodo_pipeline/directional_gait_rules.json')
        graph=build_graph(contracts,rules);nodes=graph['nodes']
        for name in PROFILES:
            self.assertEqual(nodes['motion:'+name]['op'],'mantle')
            candidate=nodes['candidate:'+name]
            self.assertIn('mantleReference',candidate['params'])
            self.assertIsNone(candidate['params']['rootPath'])
            self.assertTrue(candidate['params'].get('postProcessing',True))
            self.assertEqual(int(candidate['params']['seconds']*candidate['params']['fps']),
                max(60,reference_input(name)[0]['sampledKeyCount']))
            self.assertNotIn('motion:Stand_Pose',candidate['deps'])
        mantle_deliveries=[n for n in nodes.values() if n['op']=='deliver' and n['params']['contract']['family']=='mantle']
        self.assertEqual(len(mantle_deliveries),6)
        self.assertTrue(all(n['params']['contract']['targetMantlePolicy']==POLICY for n in mantle_deliveries))
        self.assertTrue(nodes['candidate:Walk_Forward']['params'].get('postProcessing',True))

    def test_unknown_mantle_cannot_fall_back_to_locomotion(self):
        from .mantle import profile
        with self.assertRaisesRegex(ValueError,'Unconfigured mantle'):
            profile('Mantle_Custom')

    def test_source_palm_rotation_failure_keeps_rejection_receipt(self):
        rig=soma_rig();guide,_=native_reference('Mantle_Low_Left',rig)
        local=guide['local_rot_mats'].copy();hand=rig['jointNames'].index('RightHand')
        local[:,hand]=local[:,hand]@Rotation.from_euler('x',90,degrees=True).as_matrix()
        bad=complete(local,guide['root_positions'],rig,float(guide['fps']))
        with self.assertRaises(MantleError) as error:
            author(bad,guide,rig,'Mantle_Low_Left')
        failed=next(c for c in error.exception.receipt['checks'] if c['check']=='rightHandOrientationDegrees')
        self.assertFalse(failed['passed'])
        self.assertGreater(failed['value'],80)

    def test_activation_cannot_omit_mantle_quality(self):
        from .publish import validate_mantle_pack
        with self.assertRaisesRegex(ValueError,'Missing complete per-asset mantle'):
            validate_mantle_pack(Path('unused.json'),dict(files=[]))

    def test_high_hands_release_independently_and_late_contact_is_gated(self):
        raw,skel,_=reference_input('Mantle_High')
        _,_,_,report=target(raw,skel,'Mantle_High')
        report['finalRawSemanticSha256']='controlled-test'
        curves=contact_curves(report,raw)
        self.assertEqual(curves['keys'][45]['leftHandSupport'],1.)
        self.assertEqual(curves['keys'][45]['rightHandSupport'],0.)
        bad=copy.deepcopy(raw);wrist=next(t for t in bad['tracks'] if t['bone']=='hand_l')
        rotations=Rotation.from_quat(wrist['rotations']).as_matrix()
        rotations[45]=rotations[45]@Rotation.from_euler('x',70,degrees=True).as_matrix()
        wrist['rotations']=Rotation.from_matrix(rotations).as_quat().tolist()
        _,_,_,failed=target(bad,skel,'Mantle_High')
        self.assertFalse(failed['passed'])
        self.assertFalse(next(c for c in failed['checks'] if c['check']=='leftHandOrientationDegrees')['passed'])

    def test_scaled_arm_cannot_pass_by_matching_wrist_tolerance(self):
        raw,skel,_=reference_input('Mantle_Low_Left')
        bad=copy.deepcopy(raw);arm=next(t for t in bad['tracks'] if t['bone']=='upperarm_r')
        arm['scales']=[[1.01,1.01,1.01]]
        _,_,_,report=target(bad,skel,'Mantle_Low_Left')
        self.assertFalse(report['passed'])
        self.assertFalse(next(c for c in report['checks'] if c['check']=='unitBoneScaleError')['passed'])

    def test_real_kimodo_loader_constrains_hip_trajectory_between_contacts(self):
        from kimodo.skeleton import SOMASkeleton30
        from kimodo.constraints import load_constraints_lst
        rig=soma_rig();skeleton=SOMASkeleton30()
        for name in PROFILES:
            guide,_=native_reference(name,rig)
            count=len(guide['root_positions'])
            conditions,contract=materialize_constraints(dict(semantic=name,seconds=count/30,fps=30),guide,rig)
            loaded=load_constraints_lst(conditions,skeleton)
            hips=next(c for c in loaded if c.name=='end-effector')
            self.assertEqual(hips.joint_names,['Hips'])
            selected=hips.frame_indices.tolist()
            self.assertEqual(selected,contract['generationHipTrajectoryFrames'])
            self.assertLessEqual(len(selected),19)
            self.assertEqual((selected[0],selected[-1]),(0,count-1))
            self.assertEqual(hips.pos_indices.tolist(),[skeleton.root_idx])
            self.assertEqual(hips.rot_indices.tolist(),[skeleton.root_idx])
            np.testing.assert_allclose(hips.global_joints_positions[:,skeleton.root_idx].numpy(),
                                       guide['root_positions'][selected],atol=1e-6)

    def test_real_kimodo_loader_keeps_hand_position_and_rotation_constraints(self):
        from kimodo.skeleton import SOMASkeleton30
        from kimodo.constraints import load_constraints_lst
        rig=soma_rig();skeleton=SOMASkeleton30()
        for name in PROFILES:
            guide,_=native_reference(name,rig)
            conditions,_=materialize_constraints(dict(semantic=name,seconds=3,fps=30),guide,rig)
            loaded=load_constraints_lst(conditions,skeleton)
            hands=[c for c in loaded if c.name in ('left-hand','right-hand')]
            self.assertEqual(len(hands),len(PROFILES[name]['hands']))
            for condition in hands:
                self.assertGreater(condition.pos_indices.numel(),0)
                self.assertGreater(condition.rot_indices.numel(),0)
                self.assertEqual(len(condition.frame_indices),len(condition.global_joints_rots))
                self.assertEqual(len(condition.frame_indices),len(condition.global_joints_positions))
                points=condition.global_joints_positions[:,skeleton.bone_index[condition.joint_names[0]]].numpy()
                self.assertLess(np.max(np.linalg.norm(points-points[0],axis=1)),.005)


if __name__=='__main__':unittest.main()

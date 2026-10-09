"""Root/capsule alignment without destroying body sway or doubling travel."""
import copy
import unittest
import numpy as np
from scipy.spatial.transform import Rotation
from .publish import world_pose
from .root_ownership import decompose, planar_trajectory


def fixture(count=31):
    names=['root','pelvis','head','foot_l','foot_r','hand_l','hand_r',
           'ik_foot_root','ik_foot_l','ik_foot_r','ik_hand_root','ik_hand_gun','ik_hand_l','ik_hand_r']
    parents=[-1,0,1,1,1,1,1,0,7,7,0,10,11,11]
    positions=[[0,0,0],[0,0,90],[0,0,40],[0,-10,-90],[0,10,-90],
               [0,-30,15],[0,30,15]]+[[0,0,0]]*7
    ref=[dict(position=p,rotation=[0,0,0,1],scale=[1,1,1]) for p in positions]
    skel=dict(rawBoneNames=names,rawParents=parents,referencePose=ref)
    raw=dict(sampledKeyCount=count,playLength=(count-1)/30,frameRateNumerator=30,frameRateDenominator=1,
        tracks=[dict(bone=n,positions=[p],rotations=[[0,0,0,1]],scales=[[1,1,1]]) for n,p in zip(names,positions)])
    return raw,skel


def track(raw,name):
    return next(t for t in raw['tracks'] if t['bone']==name)


class RootOwnershipTests(unittest.TestCase):
    def test_zero_range_far_origin_is_removed_even_with_static_root(self):
        raw,skel=fixture();track(raw,'pelvis')['positions']=[[148,5,90]]
        original=copy.deepcopy(raw)
        authored,runtime,report=decompose(raw,skel,family='gait_cycle')
        self.assertEqual(raw,original)
        np.testing.assert_allclose(report['canonicalOriginTranslationCm'],[148,5,0])
        np.testing.assert_allclose(world_pose(runtime,skel)['pelvis'][1],np.tile([0,0,90],(31,1)))
        np.testing.assert_allclose(world_pose(authored,skel)['root'][1],0)
        after=world_pose(runtime,skel)
        for container in ('ik_foot_root','ik_hand_root'):
            np.testing.assert_allclose(after[container][1],after['root'][1],atol=1e-10)
        for helper,body in (('ik_foot_l','foot_l'),('ik_hand_l','hand_l')):
            np.testing.assert_allclose(after[helper][1],after[body][1],atol=1e-10)

    def test_in_place_sway_height_and_local_rotations_are_retained(self):
        raw,skel=fixture();t=np.linspace(0,2*np.pi,31)
        p=np.column_stack((148+2*np.sin(t),1.5*np.cos(t),90+3*np.sin(2*t)))
        track(raw,'pelvis')['positions']=p.tolist()
        q=Rotation.from_euler('xyz',np.column_stack((.04*np.sin(t),.03*np.cos(t),.1*np.sin(t)))).as_quat()
        track(raw,'pelvis')['rotations']=q.tolist()
        before=world_pose(raw,skel)
        authored,runtime,report=decompose(raw,skel,family='gait_cycle')
        after=world_pose(runtime,skel);origin=np.asarray(report['canonicalOriginTranslationCm'])
        for name in ('pelvis','head','foot_l','foot_r','hand_l','hand_r'):
            np.testing.assert_allclose(after[name][1],before[name][1]-origin,atol=1e-10)
            np.testing.assert_allclose(after[name][0],before[name][0],atol=1e-12)
        np.testing.assert_allclose(track(runtime,'pelvis')['positions'],p-origin,atol=1e-10)
        np.testing.assert_allclose(world_pose(authored,skel)['root'][1],0,atol=1e-10)

    def test_travelling_path_preserves_sway_and_does_not_double_existing_root(self):
        raw,skel=fixture();t=np.linspace(0,1,31)
        track(raw,'root')['positions']=np.column_stack((120*t,np.zeros(31),np.zeros(31))).tolist()
        track(raw,'pelvis')['positions']=np.column_stack((148+2*np.sin(2*np.pi*t),
                                                         2*np.cos(2*np.pi*t),90+3*np.sin(4*np.pi*t))).tolist()
        before=world_pose(raw,skel)
        authored,runtime,report=decompose(raw,skel,family='roll',declared_root_motion=True)
        after=world_pose(authored,skel);origin=np.asarray(report['canonicalOriginTranslationCm'])
        for container in ('ik_foot_root','ik_hand_root'):
            np.testing.assert_allclose(after[container][1],after['root'][1],atol=1e-10)
            np.testing.assert_allclose(after[container][0],after['root'][0],atol=1e-12)
        self.assertLess(report['maximumAuthoredPelvisRootHorizontalCm'],6.05)
        self.assertGreater(after['root'][1][-1,0],108)
        self.assertLess(after['root'][1][-1,0],132)
        for name in ('pelvis','head','foot_l','foot_r','hand_l','hand_r'):
            np.testing.assert_allclose(after[name][1]+origin,before[name][1],atol=1e-10)
            np.testing.assert_allclose(after[name][0],before[name][0],atol=1e-12)
        self.assertEqual(authored,runtime)
        _,locked,_=decompose(raw,skel,family='gait_cycle')
        locked_world=world_pose(locked,skel)
        np.testing.assert_allclose(locked_world['root'][1][:,:2],0,atol=1e-12)
        np.testing.assert_allclose(locked_world['pelvis'][1][:,2],before['pelvis'][1][:,2],atol=1e-12)

    def test_root_yaw_factorization_preserves_body_pitch_roll_and_helper_targets(self):
        raw,skel=fixture(4);track(raw,'pelvis')['positions']=[[50,0,90]]
        track(raw,'pelvis')['rotations']=Rotation.from_euler('xyz',[[.2,.3,.4]]).as_quat().tolist()
        before=world_pose(raw,skel)
        root_rotation=Rotation.from_euler('z',np.linspace(0,1.5,4)[:,None]).as_matrix()
        authored,_,report=decompose(raw,skel,family='turn',root_rotation=root_rotation)
        after=world_pose(authored,skel);origin=np.asarray(report['canonicalOriginTranslationCm'])
        for name in ('pelvis','head','foot_l','hand_l'):
            np.testing.assert_allclose(after[name][0],before[name][0],atol=1e-12)
            np.testing.assert_allclose(after[name][1],before[name][1]-origin,atol=1e-10)
        for helper,body in (('ik_foot_l','foot_l'),('ik_foot_r','foot_r'),
                            ('ik_hand_gun','hand_r'),('ik_hand_l','hand_l'),('ik_hand_r','hand_r')):
            np.testing.assert_allclose(after[helper][1],after[body][1],atol=1e-10)
            np.testing.assert_allclose(after[helper][0],after[body][0],atol=1e-12)

    def test_mantle_height_does_not_add_planar_travel_twice(self):
        raw,skel=fixture(4);track(raw,'root')['positions']=[[i*10,0,0] for i in range(4)]
        track(raw,'pelvis')['positions']=[[100,0,90+i*20] for i in range(4)]
        before=world_pose(raw,skel)
        authored,runtime,report=decompose(raw,skel,family='mantle')
        after=world_pose(authored,skel)
        np.testing.assert_allclose(after['root'][1],[[i*10,0,i*20] for i in range(4)])
        np.testing.assert_allclose(after['pelvis'][1],before['pelvis'][1]-[100,0,0])
        self.assertEqual(authored,runtime)

    def test_discrete_pose_keys_are_not_interpreted_as_a_motion_path(self):
        raw,skel=fixture(4);track(raw,'pelvis')['positions']=[[0,0,90],[10,0,70],[-10,10,60],[0,20,80]]
        before=world_pose(raw,skel)
        authored,_,report=decompose(raw,skel,family='pose_table')
        after=world_pose(authored,skel)
        self.assertEqual(report['method'],'discrete-pose-no-trajectory')
        np.testing.assert_array_equal(report['canonicalOriginTranslationCm'],[0,0,0])
        np.testing.assert_allclose(after['pelvis'][1],before['pelvis'][1])

    def test_invalid_planar_samples_cannot_make_a_receipt(self):
        with self.assertRaises(ValueError):planar_trajectory([[np.nan,0]])

    def test_discrete_origins_preserve_slots_and_additive_translation(self):
        raw,skel=fixture(4)
        positions=np.array([[2,350,90],[3,351,80],[1,349,70],[4,352,90]])
        track(raw,'pelvis')['positions']=positions.tolist()
        reference={'source':'gait-master','sourceTimeSeconds':.25}
        authored,_,report=decompose(raw,skel,family='gait_pose',canonical_origin=[2,350,0],origin_reference=reference)
        np.testing.assert_allclose(world_pose(authored,skel)['pelvis'][1],positions-[2,350,0])
        np.testing.assert_allclose(world_pose(authored,skel)['root'][1],0)
        self.assertEqual(report['canonicalOriginReference'],reference)
        variant=copy.deepcopy(raw);track(variant,'pelvis')['positions']=(positions+[1,-2,0]).tolist()
        changed,_,_=decompose(variant,skel,family='lean_additive',canonical_origin=[2,350,0])
        np.testing.assert_allclose(world_pose(changed,skel)['pelvis'][1]-world_pose(authored,skel)['pelvis'][1],
                                  np.tile([1,-2,0],(4,1)))

    def test_invalid_or_continuous_inherited_origin_is_rejected(self):
        raw,skel=fixture()
        for origin in ([0,0,1],[np.nan,0,0],[0,0]):
            with self.assertRaises(ValueError):decompose(raw,skel,family='gait_pose',canonical_origin=origin)
        with self.assertRaisesRegex(ValueError,'owns its own trajectory'):
            decompose(raw,skel,family='gait_cycle',canonical_origin=[0,0,0])

    def test_implicit_reference_scales_can_be_written_to_fbx_without_changing_pose(self):
        raw,skel=fixture();track(raw,'head')['scales']=[]
        skel['referencePose'][2]['scale']=[1.2,1.2,1.2]
        before=world_pose(raw,skel)
        authored,_,_=decompose(raw,skel,family='pose_table')
        self.assertEqual(track(authored,'head')['scales'],[[1.2,1.2,1.2]])
        np.testing.assert_allclose(world_pose(authored,skel)['head'][2],before['head'][2])


if __name__=='__main__':unittest.main()

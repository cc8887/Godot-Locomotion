"""Native-reference geometry and immutable-input boundary checks."""
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np

from .als_reference import native_gait_reference,original_input
from .graph import read_json
from .gait import foot_landmarks
from .publish import world_pose
from .test_gait import walking_fixture
from .motion import fk, load_motion
from .offline_retarget import convert

ROOT=Path(__file__).resolve().parents[2]


class NativePoseReferenceTests(unittest.TestCase):
    def test_constraint_seam_smoothing_is_bounded_periodic_and_keeps_source(self):
        import copy
        from .als_reference import smooth_constraint_seam
        source,rig=walking_fixture(seconds=5)
        unchanged=copy.deepcopy(source)
        fixed,receipt=smooth_constraint_seam(source,rig,.6,60,2)
        self.assertFalse(receipt['deliverable'])
        self.assertLessEqual(receipt['maximumRotationCorrectionDegrees'],20.)
        self.assertLessEqual(receipt['maximumRootCorrectionMeters'],.05)
        np.testing.assert_array_equal(source['local_rot_mats'],unchanged['local_rot_mats'])
        np.testing.assert_array_equal(source['root_positions'],unchanged['root_positions'])
        np.testing.assert_allclose(fixed['local_rot_mats'][0:60],fixed['local_rot_mats'][60:120],atol=1e-12)
        _,joints=fk(fixed['local_rot_mats'],fixed['root_positions'],rig)
        for joint,parent in enumerate(rig['parents']):
            if parent>=0:
                np.testing.assert_allclose(np.linalg.norm(joints[:,joint]-joints[:,parent],axis=-1),
                    np.linalg.norm(rig['offsets'][joint]),atol=1e-10)
        for radius in (True,8,0):
            with self.assertRaises(ValueError):smooth_constraint_seam(source,rig,.6,60,radius)
        upper,_=smooth_constraint_seam(source,rig,.6,60,2,mask='upper_body')
        legs=[i for i,name in enumerate(rig['jointNames']) if name=='Hips' or any(token in name for token in ('Leg','Shin','Foot','Toe'))]
        np.testing.assert_array_equal(upper['local_rot_mats'][:,legs],source['local_rot_mats'][:,legs])
        np.testing.assert_allclose(upper['root_positions'],source['root_positions'],atol=1e-12)

    def test_actual_soma_arm_reference_inverts_outgoing_neutral_alignment(self):
        receipts=read_json(ROOT/'artifacts/kimodo_als/nonprop-regeneration-20261009-v1/combined-receipts.json')
        rig=read_json(receipts['rig:soma']['outputs']['rig.json']['path'])
        path=ROOT/'assets/config/refactored_animation_sources.json';index=read_json(path)
        row=next(r for r in index['assets'] if r['source'].endswith('.A_Als_Walk_Forward'))
        raw=read_json(path.parent/row['file'])['raw']
        skeleton=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']
        reference,_=native_gait_reference(path,row['source'],rig,1.5,[0,1])
        old=load_motion(receipts['reference:Walk_Forward:native-als']['outputs']['motion.npz']['path'],rig)
        np.testing.assert_array_equal(reference['root_positions'],old['root_positions'])
        legs=[i for i,name in enumerate(rig['jointNames']) if any(token in name for token in ('Leg','Shin','Foot','Toe'))]
        np.testing.assert_allclose(reference['local_rot_mats'][:,legs],old['local_rot_mats'][:,legs],atol=1e-12,rtol=0)
        native=world_pose(raw,skeleton)
        timing={key:raw[key] for key in ('sampledKeyCount','frameRateNumerator','frameRateDenominator','playLength')}
        trimmed={key:value[:raw['sampledKeyCount']] if getattr(value,'ndim',0)>0 else value for key,value in reference.items()}
        reconstructed,_=convert(trimmed,rig,skeleton,row['source'],timing)
        returned=world_pose(reconstructed,skeleton)
        _,positions=fk(reference['local_rot_mats'],reference['root_positions'],rig)
        for side,suffix in [('Left','l'),('Right','r')]:
            a,b=(rig['jointNames'].index(side+n) for n in ('Arm','ForeArm'))
            vector=positions[:raw['sampledKeyCount']-1,b]-positions[:raw['sampledKeyCount']-1,a]
            original=native['lowerarm_'+suffix][1][:-1]-native['upperarm_'+suffix][1][:-1]
            np.testing.assert_allclose(vector/np.linalg.norm(vector,axis=1)[:,None],
                original[:,[0,2,1]]/np.linalg.norm(original,axis=1)[:,None],atol=2e-7,rtol=0)
            for bone in ('clavicle','upperarm','lowerarm','hand'):
                np.testing.assert_allclose(returned[bone+'_'+suffix][0],native[bone+'_'+suffix][0],atol=1e-9)

    def test_reference_preserves_native_toe_positions_and_fixed_bone_lengths(self):
        path=ROOT/'assets/config/refactored_animation_sources.json'
        index=read_json(path)
        row=next(r for r in index['assets'] if r['source'].endswith('.A_Als_Walk_Left_Forward'))
        _,rig=walking_fixture()
        reference,receipt=native_gait_reference(path,row['source'],rig,1.5,[1,0])
        raw=read_json(path.parent/row['file'])['raw'];count=raw['sampledKeyCount']
        skeleton=index['skeletons']['/ALS/ALS/Character/SK_Als.SK_Als']
        world=world_pose(raw,skeleton);bind=world_pose(dict(sampledKeyCount=1,tracks=[]),skeleton)
        actual,_=foot_landmarks(reference,rig)
        for side,label in enumerate(('l','r')):
            bone='ball_'+label;r,p,_=world[bone];br,bp,_=bind[bone]
            offset=br[0].T@np.array([0,0,-bp[0,2]])
            desired=(p+np.einsum('tij,j->ti',r,offset))[:,[0,2,1]]*.01
            desired[:,0]+=np.arange(count)/float(reference['fps'])*1.5
            np.testing.assert_allclose(actual[:count-1,side,1],desired[:-1],atol=1e-9)
        self.assertFalse(receipt['deliverable'])
        self.assertFalse(receipt['productionReady'])

    def test_changed_or_escaping_native_payload_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);payload=root/'raw.json';payload.write_text('{}')
            index=root/'index.json';row=dict(source='source',file='raw.json',sha256='0'*64)
            index.write_text(json.dumps(dict(assets=[row])))
            with self.assertRaisesRegex(ValueError,'Changed immutable'):
                original_input(index,'source')
            row.update(file='../raw.json',sha256=hashlib.sha256(b'{}').hexdigest())
            index.write_text(json.dumps(dict(assets=[row])))
            with self.assertRaisesRegex(ValueError,'escapes config'):
                original_input(index,'source')


if __name__=='__main__':unittest.main()

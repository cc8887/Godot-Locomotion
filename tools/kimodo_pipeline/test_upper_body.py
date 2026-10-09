"""Idle-relative posture limits reject spread arms without freezing normal swing."""
import unittest
import tempfile
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation
from .graph import read_json
from .publish import ROOT
from .motion import load_motion, complete
from .als_reference import native_gait_reference
from .upper_body import audit_idle_relative
from .runner import write_json,file_hash
from .publish import validate_gait_receipt
from .motion import save_motion


class IdleRelativeArmsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.receipts=read_json(ROOT/'artifacts/kimodo_als/nonprop-regeneration-20261009-v1/combined-receipts.json')
        cls.rig=read_json(cls.receipts['rig:soma']['outputs']['rig.json']['path'])
        cls.idle=load_motion(cls.receipts['motion:Idle']['outputs']['motion.npz']['path'],cls.rig)
        cls.fixed,_=native_gait_reference(ROOT/'assets/config/refactored_animation_sources.json',
            '/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward',cls.rig,1.5,[0,1])

    def test_natural_native_swing_passes_but_old_spread_reference_fails(self):
        old=load_motion(self.receipts['reference:Walk_Forward:native-als']['outputs']['motion.npz']['path'],self.rig)
        self.assertTrue(audit_idle_relative(self.fixed,self.idle,self.rig)['passed'])
        self.assertFalse(audit_idle_relative(old,self.idle,self.rig)['passed'])
        result=audit_idle_relative(self.fixed,self.idle,self.rig)
        self.assertGreater(result['observed'][0]['maximumArmElevationDegrees'],
            result['observed'][0]['medianArmElevationDegrees']+2)

    def test_travel_and_yaw_do_not_change_arm_measurement(self):
        yaw=Rotation.from_euler('y',.7).as_matrix()
        local=self.fixed['local_rot_mats'].copy();local[:,0]=yaw@local[:,0]
        root=self.fixed['root_positions']@yaw.T+np.array([123.,0.,-72.])
        transformed=complete(local,root,self.rig,float(self.fixed['fps']))
        before=audit_idle_relative(self.fixed,self.idle,self.rig)
        after=audit_idle_relative(transformed,self.idle,self.rig)
        for a,b in zip(before['checks'],after['checks']):
            self.assertAlmostEqual(a['observed'],b['observed'],places=9)
        self.assertTrue(after['passed'])

    def test_invalid_or_unknown_limits_reject(self):
        for policy in ({'maximumArmLiftFromIdleDegrees':float('nan')},{'invented':3}):
            with self.assertRaises(ValueError):audit_idle_relative(self.fixed,self.idle,self.rig,policy)

    def test_required_upper_body_receipt_rejects_missing_failed_stale_or_other_motion(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);motion=root/'motion.npz';save_motion(motion,self.fixed,self.rig)
            gait=root/'gait.json';write_json(gait,{'pass':True})
            trajectory=root/'trajectory.json';write_json(trajectory,dict(kind='removed_authoring_trajectory'))
            job=dict(source='walk',contract=dict(targetUpperBodyPolicy='walk-idle-relative-upper-body-v1'),
                motionPath=str(motion),motionSha256=file_hash(motion),
                gaitValidationReceipt=dict(file=str(gait),sha256=file_hash(gait),sourceMotionFile=str(motion),
                    sourceMotionSha256=file_hash(motion),deliveredMotionSha256=file_hash(motion)),
                authoringTrajectory=dict(file=str(trajectory),sha256=file_hash(trajectory)))
            with self.assertRaisesRegex(ValueError,'Missing required upper-body'):validate_gait_receipt(job)
            upper=root/'upper.json';report=audit_idle_relative(self.fixed,self.idle,self.rig)
            write_json(upper,report);job['upperBodyValidationReceipt']=dict(file=str(upper),sha256=file_hash(upper))
            validate_gait_receipt(job)
            report['passed']=False;write_json(upper,report)
            with self.assertRaisesRegex(ValueError,'Changed upper-body'):validate_gait_receipt(job)
            job['upperBodyValidationReceipt']['sha256']=file_hash(upper)
            with self.assertRaisesRegex(ValueError,'Upstream upper-body'):validate_gait_receipt(job)
            report.update(passed=True,motionSha256='0'*64);write_json(upper,report)
            job['upperBodyValidationReceipt']['sha256']=file_hash(upper)
            with self.assertRaisesRegex(ValueError,'differs from validated'):validate_gait_receipt(job)


if __name__=='__main__':unittest.main()

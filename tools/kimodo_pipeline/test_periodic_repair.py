"""Independent geometric checks for bounded periodic cleanup."""
import copy
import unittest
import numpy as np
from scipy.spatial.transform import Rotation

from .gait import foot_landmarks,audit_candidate
from .motion import complete, fk
from .periodic_repair import repair_periodic
from .test_gait import walking_fixture


class NativeSoleCalibrationTests(unittest.TestCase):
    def test_bvh_export_basis_does_not_rotate_physical_rest_landmarks(self):
        source, rig = walking_fixture()
        rig = copy.deepcopy(rig)
        rig.pop('footLandmarks')
        rig['standardTPoseGlobalRotations'] = np.tile(
            Rotation.from_euler('x', 90, degrees=True).as_matrix(), (len(rig['jointNames']), 1, 1)).tolist()
        points, details = foot_landmarks(source, rig)
        rig['standardTPoseGlobalRotations'] = np.tile(np.eye(3), (len(rig['jointNames']), 1, 1)).tolist()
        other, _ = foot_landmarks(source, rig)
        np.testing.assert_allclose(points, other, atol=1e-12)
        self.assertLess(details['definitions']['left']['heel']['offset'][1], 0)


class PeriodicRepairTests(unittest.TestCase):
    def test_planned_reference_support_does_not_replace_measured_output_gate(self):
        reference,rig=walking_fixture(seconds=5.)
        source=copy.deepcopy(reference)
        source['root_positions'][:,0]+=.03*np.sin(np.arange(len(source['root_positions']))/60*2*np.pi)
        cycle=audit_candidate(reference,rig)['cycles'][0]
        span=(cycle['start'],cycle['endInclusive'])
        fixed,report=repair_periodic(source,rig,.6,harmonics=4,target_period_frames=60,
            constrained_cycle_span=span,contact_reference=reference,enforce_sole_clearance=True)
        self.assertTrue(report['after']['pass'])
        self.assertEqual('independently measured generation-reference heel/toe intervals',report['contactPlanSource'])
        self.assertLessEqual(report['maximumAnkleCorrectionMeters'],.15)
        self.assertLessEqual(report['maximumHipLoweringMeters'],.05)
        with self.assertRaisesRegex(ValueError,'correction exceeds bound'):
            repair_periodic(source,rig,.6,harmonics=4,constrained_cycle_span=span,
                contact_reference=reference,max_ankle_correction=1e-7)

    def test_constraint_window_requires_matching_output_pose_and_measured_contacts(self):
        source,rig=walking_fixture(seconds=5.)
        with self.assertRaisesRegex(ValueError,'independently measured'):
            repair_periodic(source,rig,.6,constrained_cycle_span=(60,120))
        damaged=copy.deepcopy(source);damaged['local_rot_mats'][120,1]=Rotation.from_euler('x',120,degrees=True).as_matrix()
        with self.assertRaisesRegex(ValueError,'endpoints do not match'):
            repair_periodic(damaged,rig,.6,constrained_cycle_span=(60,120),contact_reference=source)

    def test_period_change_repairs_stance_after_retiming(self):
        source, rig = walking_fixture(seconds=5.)
        repaired, report = repair_periodic(source, rig, .6, harmonics=4,target_period_frames=72)
        self.assertTrue(report['after']['pass'])
        self.assertEqual(72,report['outputPeriodFrames'])
        self.assertEqual(361,len(repaired['root_positions']))
        displacement=repaired['root_positions'][72]-repaired['root_positions'][0]
        self.assertAlmostEqual(.72,float(np.linalg.norm(displacement[[0,2]])),places=8)

    def test_cleanup_preserves_bone_lengths_and_periodic_motion(self):
        source, rig = walking_fixture(seconds=5.)
        root = source['root_positions'].copy()
        root[:, 0] += np.arange(len(root))/60*.08
        shifted = complete(source['local_rot_mats'], root, rig, 60)
        repaired, report = repair_periodic(shifted, rig, .6, harmonics=4)
        self.assertTrue(report['after']['pass'])
        _, positions = fk(repaired['local_rot_mats'], repaired['root_positions'], rig)
        for j, parent in enumerate(rig['parents']):
            if parent >= 0:
                np.testing.assert_allclose(np.linalg.norm(positions[:, j]-positions[:, parent], axis=-1),
                                           np.linalg.norm(rig['offsets'][j]), atol=1e-10)
        period = report['sourceCycle']['endInclusive']-report['sourceCycle']['start']
        np.testing.assert_allclose(repaired['local_rot_mats'][period:2*period],
                                   repaired['local_rot_mats'][2*period:3*period], atol=1e-9)
        self.assertFalse(report['productionReady'])

    def test_large_repair_does_not_silently_clip_or_stretch(self):
        source, rig = walking_fixture(seconds=5.)
        with self.assertRaisesRegex(ValueError, 'correction exceeds bound'):
            repair_periodic(source, rig, .6, max_ankle_correction=1e-7)


if __name__ == '__main__':
    unittest.main()

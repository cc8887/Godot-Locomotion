"""Check the actual FK targets emitted for Kimodo, not only JSON structure."""
from __future__ import annotations

import copy
import unittest

import numpy as np
from scipy.spatial.transform import Rotation

from .gait_constraints import GaitConstraintError, materialize_gait_constraints
from .motion import complete, fk
from .test_gait import walking_fixture


def parameters(speed=.6):
    return {'seconds': 5., 'fps': 60, 'targetPeriodSeconds': 1.,
            'rootPath': {'kind': 'linear', 'velocity': speed, 'direction': [0., 1.], 'headingDegrees': 0}}


def decode(constraint, rig):
    frames = constraint['frame_indices']
    axis = np.asarray(constraint['local_joints_rot'])
    local = Rotation.from_rotvec(axis.reshape(-1, 3)).as_matrix().reshape(len(frames), -1, 3, 3)
    root = np.asarray(constraint['root_positions'])
    global_rot, positions = fk(local, root, rig)
    return {frame: (local[index], root[index], global_rot[index], positions[index])
            for index, frame in enumerate(frames)}


class GaitConstraintTests(unittest.TestCase):
    def test_hand_endpoint_orientation_targets_repeat_with_the_body_cycle(self):
        source,rig=walking_fixture(seconds=5)
        rig=copy.deepcopy(rig)
        for label,x in (('Left',.3),('Right',-.3)):
            rig['jointNames'].append(label+'Hand');rig['parents'].append(0);rig['offsets'].append([x,.4,.1])
        local=np.concatenate([source['local_rot_mats'],np.tile(np.eye(3),(len(source['root_positions']),2,1,1))],axis=1)
        local[:,-2]=Rotation.from_euler('z',(.2*np.sin(np.arange(len(local))/60*2*np.pi))[:,None]).as_matrix()
        source=complete(local,source['root_positions'],rig,60)
        params=parameters();params.update(seconds=3.02,fullBodyPhaseFractions=[.25,.5,.75],
            fullBodyPhaseCycles=3,constrainHandOrientations=True)
        constraints,receipt=materialize_gait_constraints(params,source,rig)
        for side in ('left','right'):
            poses=decode(next(c for c in constraints if c['type']==side+'-hand'),rig)
            hand=rig['jointNames'].index(side.title()+'Hand')
            for a,b in ((0,60),(15,75),(30,90)):
                np.testing.assert_allclose(poses[a][2][hand],poses[b][2][hand],atol=1e-10)
            self.assertLess(receipt['constraintCounts'][side+'-hand'],20)

    def test_repeated_phase_poses_and_support_interiors_are_measured(self):
        source,rig=walking_fixture(seconds=5)
        params=parameters();params.update(seconds=3.02,fullBodyPhaseFractions=[.25,.5,.75],
            fullBodyPhaseCycles=3,supportAnchorCount=4,maximumAnchorCorrectionMeters=.15)
        constraints,receipt=materialize_gait_constraints(params,source,rig)
        self.assertTrue(receipt['pass'])
        poses=decode(next(c for c in constraints if c['type']=='fullbody'),rig)
        for phase in (15,30,45):
            np.testing.assert_allclose(poses[phase][0],poses[phase+60][0],atol=1e-12)
        for pair in receipt['supportPairs']:
            self.assertEqual(4,len(pair['frames']))
            decoded=decode(next(c for c in constraints if c['type']==pair['side']+'-foot'),rig)
            foot=rig['jointNames'].index(pair['side'].title()+'Foot')
            for frame in pair['frames']:np.testing.assert_allclose(decoded[frame][3][foot],pair['anchorMeters'],atol=1e-10)
        self.assertLess(max(receipt['constraintCounts'].values()),20)

    def test_boundary_neighborhoods_repeat_position_and_motion_context(self):
        motion, rig = walking_fixture(seconds=5.)
        params = parameters(); params['boundaryContextFrames'] = 1
        constraints, receipt = materialize_gait_constraints(params, motion, rig)
        fullbody = next(item for item in constraints if item['type'] == 'fullbody')
        poses = decode(fullbody, rig)
        self.assertLess(len(poses), 20)
        for offset in (-1, 0, 1):
            a, b = poses[60+offset], poses[120+offset]
            np.testing.assert_allclose(a[0], b[0], atol=1e-12)
            np.testing.assert_allclose(b[3]-a[3], np.tile([0., 0., .6], (len(rig['jointNames']), 1)), atol=1e-12)
        before = poses[60][3]-poses[59][3]
        after = poses[120][3]-poses[119][3]
        np.testing.assert_allclose(before, after, atol=1e-12)
        self.assertTrue(receipt['pass'])

    def test_support_anchors_are_world_fixed_while_hips_advance(self):
        motion, rig = walking_fixture(seconds=5.)
        constraints, receipt = materialize_gait_constraints(parameters(), motion, rig)
        self.assertTrue(receipt['pass'])
        self.assertFalse(receipt['referenceAcceptedAsMaster'])
        by_type = {item['type']: item for item in constraints}
        self.assertEqual(set(by_type), {'root2d', 'fullbody', 'left-foot', 'right-foot'})
        for kind in ('fullbody', 'left-foot', 'right-foot'):
            self.assertLess(len(by_type[kind]['frame_indices']), 20)
        for pair in receipt['supportPairs']:
            decoded = decode(by_type[pair['side'] + '-foot'], rig)
            foot = rig['jointNames'].index(pair['side'].title() + 'Foot')
            toe = rig['jointNames'].index(pair['side'].title() + 'ToeEnd')
            first, last = [decoded[frame] for frame in pair['frames']]
            self.assertGreater(last[1][2] - first[1][2], .01)
            np.testing.assert_allclose(first[3][foot], last[3][foot], atol=1e-10)
            np.testing.assert_allclose(first[3][toe], last[3][toe], atol=1e-10)
            np.testing.assert_allclose(first[2][foot], last[2][foot], atol=1e-10)
            np.testing.assert_allclose(first[3][foot], pair['anchorMeters'], atol=1e-10)

    def test_fullbody_cycle_boundaries_repeat_pose_plus_actual_travel(self):
        motion, rig = walking_fixture(seconds=5.)
        constraints, _ = materialize_gait_constraints(parameters(), motion, rig)
        fullbody = next(item for item in constraints if item['type'] == 'fullbody')
        poses = decode(fullbody, rig)
        frames = fullbody['frame_indices']
        for start, end in zip(frames, frames[1:]):
            np.testing.assert_allclose(poses[start][0], poses[end][0], atol=1e-12)
            np.testing.assert_allclose(poses[end][3] - poses[start][3],
                                       np.tile([0., 0., .6], (len(rig['jointNames']), 1)), atol=1e-12)

    def test_all_constraint_roots_and_headings_match_dense_root2d(self):
        motion, rig = walking_fixture(seconds=5.)
        params = parameters(); params['rootPath']['headingDegrees'] = 37
        params['rootPath']['direction'] = [.6, .8]
        constraints, receipt = materialize_gait_constraints(params, motion, rig)
        path = np.asarray(constraints[0]['smooth_root_2d'])
        for constraint in constraints[1:]:
            for frame, (_, root, _, joints) in decode(constraint, rig).items():
                np.testing.assert_allclose(root[[0, 2]], path[frame], atol=1e-12)
                hip_line = joints[5] - joints[1]
                self.assertAlmostEqual(np.arctan2(hip_line[2], -hip_line[0]), np.deg2rad(37), places=10)
        self.assertLess(receipt['maximumHeadingErrorRadians'], 1e-10)

    def test_reference_rejection_is_not_relabelled_master_acceptance(self):
        motion, rig = walking_fixture(seconds=1.5)
        original = copy.deepcopy(motion)
        constraints, receipt = materialize_gait_constraints(parameters(), motion, rig)
        self.assertTrue(constraints)
        self.assertFalse(receipt['referenceAuditPass'])
        self.assertFalse(receipt['referenceAcceptedAsMaster'])
        np.testing.assert_array_equal(motion['root_positions'], original['root_positions'])
        np.testing.assert_array_equal(motion['local_rot_mats'], original['local_rot_mats'])

    def test_unreachable_fixed_foot_anchor_is_a_failure(self):
        motion, rig = walking_fixture(seconds=5.)
        with self.assertRaises(GaitConstraintError) as failure:
            materialize_gait_constraints(parameters(speed=30.), motion, rig)
        self.assertIn('No reachable support pair', str(failure.exception))
        self.assertFalse(failure.exception.receipt['pass'])
        self.assertFalse(failure.exception.receipt['referenceAcceptedAsMaster'])

    def test_missing_observed_cycle_cannot_be_replaced_with_percent_crop(self):
        motion, rig = walking_fixture(seconds=5.)
        motion['root_positions'][:, 1] += 2
        with self.assertRaisesRegex(GaitConstraintError, 'no complete alternating'):
            materialize_gait_constraints(parameters(), motion, rig)

    def test_shared_hip_lowering_is_explicit_and_never_exceeds_configured_limit(self):
        motion, rig = walking_fixture(seconds=5.)
        params = parameters(speed=10.)
        constraints, receipt = materialize_gait_constraints(params, motion, rig)
        maximum_drop = max(item['dropMeters'] for item in receipt['hipHeightAdjustments'])
        self.assertGreater(maximum_drop, .005)
        self.assertLessEqual(maximum_drop, .03)
        by_frame = {}
        for constraint in constraints[1:]:
            for frame, (_, root, _, _) in decode(constraint, rig).items():
                if frame in by_frame:
                    np.testing.assert_allclose(root, by_frame[frame], atol=1e-12)
                by_frame[frame] = root
        params['maximumHipLoweringMeters'] = .005
        with self.assertRaisesRegex(GaitConstraintError, 'hip-lowering limit'):
            materialize_gait_constraints(params, motion, rig)

    def test_sparse_frame_limit_is_enforced_before_generation(self):
        motion, rig = walking_fixture(seconds=5.)
        params = parameters(); params['seconds'] = 15
        with self.assertRaisesRegex(GaitConstraintError, 'frame budget'):
            materialize_gait_constraints(params, motion, rig)

    def test_nonunit_path_and_subframe_cycle_are_rejected(self):
        motion, rig = walking_fixture()
        params = parameters(); params['rootPath']['direction'] = [0., 2.]
        with self.assertRaisesRegex(ValueError, 'unit XZ'):
            materialize_gait_constraints(params, motion, rig)
        params = parameters(); params['targetPeriodSeconds'] = 1.001
        with self.assertRaisesRegex(ValueError, 'integral'):
            materialize_gait_constraints(params, motion, rig)


if __name__ == '__main__':
    unittest.main()

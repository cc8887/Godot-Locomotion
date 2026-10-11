"""Independent kinematic fixtures and corruptions for authoring gate behavior."""
from __future__ import annotations

import copy
import unittest

import numpy as np
from scipy.spatial.transform import Rotation

from .gait import (GaitValidationError, audit_candidate, author_cycle,
                   compile_foot_planted, detect_foot_contacts, detect_gait_events,
                   revalidate_authored_cycle, _array_hash)
from .motion import complete


def walking_fixture(period=1., seconds=4., fps=60, speed=.6):
    """Two-link analytic legs walking with fixed stance anchors and smooth swing.

    This is a mathematical test fixture, never an authoring or exported asset.
    Joint rotations are solved from prescribed world foot paths independently
    of the event detector and metadata compiler under test.
    """
    names = ['Hips', 'LeftLeg', 'LeftShin', 'LeftFoot', 'LeftToeEnd',
             'RightLeg', 'RightShin', 'RightFoot', 'RightToeEnd']
    rig = {'jointNames': names, 'parents': [-1, 0, 1, 2, 3, 0, 5, 6, 7],
           'offsets': [[0, 0, 0], [.1, 0, 0], [0, -.5, 0], [0, -.5, 0], [0, -.1, .15],
                       [-.1, 0, 0], [0, -.5, 0], [0, -.5, 0], [0, -.1, .15]],
           'footJointNames': ['LeftFoot', 'LeftToeEnd', 'RightFoot', 'RightToeEnd'],
           'footLandmarks': {side: {'heel': {'joint': side.title() + 'Foot', 'offset': [0, -.1, -.04]},
                                    'toe': {'joint': side.title() + 'ToeEnd', 'offset': [0, 0, 0]}}
                             for side in ('left', 'right')}}
    count = round(seconds * fps) + 1
    times = np.arange(count) / fps
    local = np.broadcast_to(np.eye(3), (count, len(names), 3, 3)).copy()
    roots = np.stack([np.zeros(count), np.full(count, .98), speed * times], axis=1)
    for side, leg in enumerate((1, 5)):
        offset = .5 * side + .15
        phase_time = times / period - offset
        phase = phase_time % 1
        cycle = np.floor(phase_time)
        # The foot remains world-fixed for 60% of a cycle, then advances one
        # full stride. Quintic easing makes touchdown/liftoff velocity zero.
        anchor = speed * period * (cycle + offset + .3)
        swing = np.clip((phase - .6) / .4, 0., 1.)
        easing = 6 * swing**5 - 15 * swing**4 + 10 * swing**3
        foot_z = anchor + speed * period * easing
        foot_y = .1 + .15 * np.sin(np.pi * swing)**2
        dy, dz = foot_y - roots[:, 1], foot_z - roots[:, 2]
        distance = np.hypot(dy, dz)
        if np.max(distance) >= 1:
            raise ValueError('Fixture foot outside two-link reach')
        knee = np.arccos((distance**2 - .5) / .5)
        hip = np.arctan2(-dz, -dy) - knee / 2
        local[:, leg] = Rotation.from_euler('x', hip[:, None]).as_matrix()
        local[:, leg + 1] = Rotation.from_euler('x', knee[:, None]).as_matrix()
        local[:, leg + 2] = Rotation.from_euler('x', (-hip - knee)[:, None]).as_matrix()
    return complete(local, roots, rig, fps), rig


class ContactDetectionTests(unittest.TestCase):
    def test_cyclic_statistics_use_each_frame_once_and_ignore_start_phase(self):
        period = 24
        t = np.arange(period) / 30.
        points = np.zeros((period, 2, 2, 3))
        points[..., 1] = (.04 + .025 * np.sin(2 * np.pi * t / (period / 30.)))[:, None, None]
        points[..., 2] = (.1 * np.sin(2 * np.pi * t / (period / 30.)))[:, None, None]
        velocity = (np.roll(points, -1, axis=0) - np.roll(points, 1, axis=0)) * 15.
        near = points[:, 0, :, 1] <= .055
        expected = np.median(np.linalg.norm(velocity[:, 0][:, :, [0, 2]], axis=-1)[near])
        for shift in (0, 5, 12, 23):
            rotated = np.roll(points, shift, axis=0)
            detected = detect_foot_contacts(np.concatenate([rotated, rotated[:1]]), 30.,
                                            cycle_displacement=np.zeros(3))
            self.assertEqual(period, detected['measurementFrameCount'])
            self.assertEqual(int(near.sum()), detected['measurements']['left']['nearGroundSamples'])
            self.assertAlmostEqual(expected, detected['measurements']['left']['nearGroundMedianSpeedMps'])

    def test_skating_does_not_disappear_when_contact_detector_rejects_it(self):
        points = np.zeros((90, 2, 2, 3))
        points[..., 2] = (np.arange(90) / 30)[:, None, None]
        detected = detect_foot_contacts(points, 30)
        for side in ('left', 'right'):
            metrics = detected['measurements'][side]
            self.assertEqual(metrics['contactFrames'], 0)
            self.assertEqual(metrics['nearGroundSamples'], 180)
            self.assertAlmostEqual(metrics['nearGroundMedianSpeedMps'], 1)

    def test_airborne_data_has_no_valid_samples(self):
        points = np.zeros((30, 2, 2, 3)); points[..., 1] = .2
        detected = detect_foot_contacts(points, 30)
        self.assertEqual(detected['events'], [])
        self.assertIsNone(detected['measurements']['left']['nearGroundMedianSpeedMps'])

    def test_one_frame_floor_crossing_is_not_support(self):
        points = np.zeros((30, 2, 2, 3)); points[..., 1] = .2
        points[15, :, :, 1] = 0
        detected = detect_foot_contacts(points, 30)
        self.assertEqual(np.sum(detected['contacts']), 0)

    def test_native_contacts_are_not_trusted(self):
        motion, rig = walking_fixture()
        before = detect_gait_events(motion, rig)
        motion['contacts'] = np.zeros((len(motion['root_positions']), 4))
        after = detect_gait_events(motion, rig)
        self.assertEqual(before, after)


class FootPlantedProtocolTests(unittest.TestCase):
    def test_full_and_partial_values_select_actual_als_stop_rules(self):
        values = compile_foot_planted(['left_stable', 'right_stable', 'plant_left', 'plant_right'])
        # Literal original stop-rule boundaries are the independent oracle.
        def original_stop_rule(value):
            if value <= -.5:
                return 'LockLeft'
            if value > .5:
                return 'LockRight'
            return 'PlantRight' if value > 0 else 'PlantLeft'
        self.assertEqual([original_stop_rule(value) for value in values],
                         ['LockLeft', 'LockRight', 'PlantLeft', 'PlantRight'])
        np.testing.assert_array_equal(values, [-1., 1., -.25, .25])

    def test_unknown_stage_cannot_be_published_as_zero(self):
        with self.assertRaisesRegex(ValueError, 'Unresolved support'):
            compile_foot_planted(['left_stable', 'unknown'])


class EventCycleTests(unittest.TestCase):
    def test_bound_pass_receipt_is_revalidated_against_actual_motion(self):
        motion, rig = walking_fixture(seconds=5.)
        master, receipt = author_cycle(motion, rig, expected_speed_mps=.6)
        self.assertTrue(revalidate_authored_cycle(master, rig, receipt)['pass'])
        master['root_positions'][:, 0] += .1 * np.sin(np.linspace(0, 2 * np.pi, len(master['root_positions'])))
        receipt['outputMotionSha256'] = _array_hash(master['local_rot_mats'], master['root_positions'], master['fps'])
        with self.assertRaises(GaitValidationError) as rejected:
            revalidate_authored_cycle(master, rig, receipt)
        self.assertTrue(any(c['name'].endswith('_geometric_slip') and not c['pass']
                            for c in rejected.exception.receipt['checks']))

    def test_analytic_world_locked_walk_passes_and_preserves_unmodified_seam(self):
        motion, rig = walking_fixture()
        before = copy.deepcopy(motion)
        output, receipt = author_cycle(motion, rig, expected_speed_mps=.6)
        self.assertTrue(receipt['pass'])
        self.assertGreaterEqual(len(receipt['cycles']), 2)
        self.assertAlmostEqual(receipt['selectedCycle']['oppositePhase'], .5, places=2)
        start, end = receipt['sourceSpan']['start'], receipt['sourceSpan']['endInclusive']
        np.testing.assert_allclose(output['local_rot_mats'], motion['local_rot_mats'][start:end + 1], atol=1e-12)
        np.testing.assert_array_equal(motion['root_positions'], before['root_positions'])
        np.testing.assert_array_equal(motion['local_rot_mats'], before['local_rot_mats'])
        reconstructed = output['root_positions'] + receipt['removedPlanarTrajectory']['positions']
        np.testing.assert_allclose(reconstructed, motion['root_positions'][start:end + 1], atol=1e-12)
        self.assertAlmostEqual(receipt['removedPlanarTrajectory']['cycleDistance'], .6)
        self.assertTrue(np.all(np.diff(receipt['removedPlanarTrajectory']['distance']) >= 0))

    def test_world_origin_is_preserved_in_inverse_trajectory_and_removed_from_body(self):
        motion, rig = walking_fixture()
        motion['root_positions'] += [4., 0., 12.]
        output, receipt = author_cycle(motion, rig, expected_speed_mps=.6)
        self.assertGreater(receipt['removedPlanarTrajectory']['positions'][0][2], 12.)
        np.testing.assert_allclose(output['root_positions'][:, [0, 2]], 0, atol=1e-12)

    def test_invalid_startup_is_excluded_by_events_when_later_cycles_are_sound(self):
        motion, rig = walking_fixture(seconds=5.)
        motion['root_positions'][:40, 1] += .25
        output, receipt = author_cycle(motion, rig, expected_speed_mps=.6)
        self.assertTrue(receipt['pass'])
        self.assertGreaterEqual(receipt['sourceSpan']['start'], 40)
        self.assertTrue(receipt['stableWindows'])
        self.assertGreater(len(output['root_positions']), 3)

    def test_missing_one_side_and_fabricated_contacts_are_rejected(self):
        motion, rig = walking_fixture()
        motion['local_rot_mats'][:, 5:8] = Rotation.from_euler('x', [[.9], [1.], [-.4]]).as_matrix()
        motion = complete(motion['local_rot_mats'], motion['root_positions'], rig, 60)
        motion['contacts'][:] = 1
        receipt = audit_candidate(motion, rig, expected_speed_mps=.6)
        self.assertFalse(receipt['pass'])
        with self.assertRaises(GaitValidationError):
            author_cycle(motion, rig, expected_speed_mps=.6)

    def test_deeply_buried_feet_are_not_valid_support(self):
        motion, rig = walking_fixture()
        motion['root_positions'][:, 1] -= .25
        receipt = audit_candidate(motion, rig, expected_speed_mps=.6)
        failed = [check['name'] for cycle in receipt['cycles'] for check in cycle['checks'] if not check['pass']]
        self.assertIn('left_penetration', failed)
        self.assertIn('right_penetration', failed)

    def test_retiming_cannot_keep_old_speed_acceptance(self):
        motion, rig = walking_fixture()
        with self.assertRaises(GaitValidationError) as failure:
            author_cycle(motion, rig, target_count=31, expected_speed_mps=.6)
        failed = [check['name'] for check in failure.exception.receipt['checks'] if not check['pass']]
        self.assertIn('retiming_ratio', failed)
        self.assertIn('retimed_authored_speed', failed)

    def test_quiet_endpoint_discontinuity_cannot_be_hidden_by_tail_blending(self):
        motion, rig = walking_fixture()
        # Slow time-dependent root-height drift keeps the foot contact detector
        # working but makes repeated same-foot poses vertically incompatible.
        motion['root_positions'][:, 1] += np.linspace(0., .11, len(motion['root_positions']))
        receipt = audit_candidate(motion, rig, expected_speed_mps=.6)
        self.assertFalse(receipt['pass'])
        with self.assertRaises(GaitValidationError):
            author_cycle(motion, rig, expected_speed_mps=.6)


if __name__ == '__main__':
    unittest.main()

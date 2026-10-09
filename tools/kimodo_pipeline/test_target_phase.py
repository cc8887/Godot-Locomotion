"""Vertical phases may seed IK before any strict contact gate can pass."""
import copy
import unittest
import tempfile
from pathlib import Path
from unittest.mock import patch

import numpy as np

from .target_phase import infer_phases
from .target_cleanup import repair_periodic_gait
from .target_gait import original_protocol, analyze_target_gait
from .publish import world_pose


class TargetPhaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.protocol = original_protocol()
        cls.original = cls.protocol['assets']['walk']
        cls.skeleton = cls.protocol['skeleton']

    def test_vertical_offset_does_not_change_inferred_phase(self):
        original = self.original['raw']; shifted = copy.deepcopy(original)
        pelvis = next(t for t in shifted['tracks'] if t['bone'].lower() == 'pelvis')
        for p in pelvis['positions']: p[2] += 20
        a = infer_phases(original, self.skeleton, self.original['source'])
        b = infer_phases(shifted, self.skeleton, self.original['source'])
        self.assertEqual(a['candidateContacts'], b['candidateContacts'])
        self.assertEqual([s['troughFrame'] for s in a['sides']], [s['troughFrame'] for s in b['sides']])

    def test_floating_walk_is_repaired_then_passes_unchanged_gate(self):
        raw = copy.deepcopy(self.original['raw'])
        pelvis = next(t for t in raw['tracks'] if t['bone'].lower() == 'pelvis')
        for p in pelvis['positions']: p[2] += 3
        before = copy.deepcopy(raw)
        self.assertFalse(analyze_target_gait(raw, self.skeleton, self.original['source'])['passed'])
        repaired, phase, receipt = repair_periodic_gait(raw, self.skeleton, self.original['source'])
        gate = analyze_target_gait(repaired, self.skeleton, self.original['source'])
        self.assertTrue(gate['passed'], [c for c in gate['checks'] if not c['passed']])
        self.assertTrue(phase['passed']); self.assertTrue(receipt['applied']); self.assertEqual(before, raw)
        a, b = world_pose(raw, self.skeleton), world_pose(repaired, self.skeleton)
        for suffix in ('l', 'r'):
            for parent, child in (('thigh', 'calf'), ('calf', 'foot')):
                np.testing.assert_allclose(np.linalg.norm(a[child+'_'+suffix][1]-a[parent+'_'+suffix][1], axis=1),
                    np.linalg.norm(b[child+'_'+suffix][1]-b[parent+'_'+suffix][1], axis=1), atol=1e-8)
        altered = {'pelvis','thigh_l','calf_l','foot_l','ball_l','thigh_r','calf_r','foot_r','ball_r'}
        for original, fixed in zip(raw['tracks'], repaired['tracks']):
            if original['bone'].lower() not in altered:
                for field in ('positions','rotations','scales'):
                    np.testing.assert_allclose(original[field], fixed[field], atol=1e-12)

    def synthetic(self, *, same_phase=False, cycles=1, offset=0):
        period = self.original['raw']['sampledKeyCount']-1
        angle = np.arange(period+1)*2*np.pi/period
        points = np.zeros((period+1, 2, 2, 3))
        points[:, 0, :, 2] = (offset+6*(1-np.cos(angle*cycles)))[:, None]
        points[:, 1, :, 2] = (offset+6*(1-np.cos(angle*cycles+(0 if same_phase else np.pi))))[:, None]
        return points

    def test_same_phase_and_double_cycle_are_rejected(self):
        for points in (self.synthetic(same_phase=True), self.synthetic(cycles=2)):
            with patch('tools.kimodo_pipeline.target_phase._sole_geometry', return_value=(points,None,None)):
                with self.assertRaises(ValueError):
                    infer_phases(self.original['raw'], self.skeleton, self.original['source'])

    def test_wrapped_low_phase_and_noise_keep_periodic_contact_identity(self):
        points = self.synthetic(offset=30)
        rng = np.random.default_rng(11)
        points[:-1, :, :, 2] += rng.normal(0, .05, points[:-1, :, :, 2].shape)
        points[-1] = points[0]
        with patch('tools.kimodo_pipeline.target_phase._sole_geometry', return_value=(points,None,None)):
            phase = infer_phases(self.original['raw'], self.skeleton, self.original['source'])
        np.testing.assert_equal(phase['candidateContacts'][0], phase['candidateContacts'][-1])
        self.assertTrue(phase['candidateContacts'][0][0])
        self.assertAlmostEqual(.5, phase['troughPhaseGap'], delta=.04)

    def test_static_geometry_is_not_a_gait_phase(self):
        points = np.zeros_like(self.synthetic())
        with patch('tools.kimodo_pipeline.target_phase._sole_geometry', return_value=(points,None,None)):
            with self.assertRaisesRegex(ValueError, 'swing excursion'):
                infer_phases(self.original['raw'], self.skeleton, self.original['source'])

    def test_excessive_repair_is_rejected_without_mutating_input(self):
        raw = copy.deepcopy(self.original['raw'])
        for track in raw['tracks']:
            if track['bone'].lower() == 'pelvis':
                for p in track['positions']: p[2] += 20
        before = copy.deepcopy(raw)
        with self.assertRaisesRegex(ValueError, 'correction exceeds bound') as caught:
            repair_periodic_gait(raw, self.skeleton, self.original['source'])
        self.assertTrue(caught.exception.phase['passed'])
        self.assertEqual(before, raw)

    def test_ik_handles_a_contact_window_across_the_loop_boundary(self):
        raw = copy.deepcopy(self.original['raw']); period = raw['sampledKeyCount']-1
        indices = np.roll(np.arange(period), -7); indices = np.r_[indices, indices[0]]
        for track in raw['tracks']:
            for key in ('positions','rotations','scales'):
                if len(track[key]) == period+1:
                    track[key] = np.asarray(track[key])[indices].tolist()
        repaired, phase, _ = repair_periodic_gait(raw, self.skeleton, self.original['source'])
        self.assertTrue(any(0 in side['windowFrames'] and period-1 in side['windowFrames'] for side in phase['sides']))
        world = world_pose(repaired, self.skeleton)
        for name in ('foot_l','foot_r','ball_l','ball_r'):
            np.testing.assert_allclose(world[name][1][0], world[name][1][-1], atol=1e-8)
            np.testing.assert_allclose(world[name][0][0], world[name][0][-1], atol=1e-8)

    def test_phase_failure_is_not_an_extra_strict_asset_quality_gate(self):
        from .publish import prepare_target_raw, CONFIG
        from .graph import read_json
        from .runner import write_json
        index=read_json(CONFIG/'refactored_animation_sources.json')
        native=self.protocol['assets']['crouch_walk_left_backward']
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'input.json'; write_json(path,native['raw'])
            job=dict(source=native['source'],edition='refactored',raw=str(path),
                     contract=dict(targetGaitPolicy='als-forward-v1'))
            with patch('tools.kimodo_pipeline.publish.validate_gait_receipt') as upstream:
                result,_=prepare_target_raw(job,self.skeleton,self.skeleton,index,factor_root=False)
            upstream.assert_called_once_with(job)
            self.assertFalse(job['_targetPhaseReport']['passed'])
            self.assertIn('do not alternate',job['_targetPhaseReport']['error'])
            self.assertFalse(job['_targetCleanupReport']['applied'])
            self.assertTrue(analyze_target_gait(result,self.skeleton,native['source'])['passed'])


if __name__ == '__main__':
    unittest.main()

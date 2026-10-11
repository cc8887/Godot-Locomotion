"""Native locomotion roles and geometric regressions for the new recipes."""
from pathlib import Path
import tempfile
import unittest

import numpy as np
from scipy.spatial.transform import Rotation

from .graph import build_graph, load_rules, read_json, topological_order
from .gait import author_cycle, mirror_authored_cycle, detect_gait_events
from .gait_constraints import materialize_gait_constraints
from .motion import complete, freeze, materialize_constraints,save_motion,load_motion
from .test_gait import walking_fixture

ROOT = Path(__file__).resolve().parents[2]


class DirectionalRecipeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.graph = build_graph(read_json(ROOT/'docs/architecture/kimodo-als-asset-contracts.json'),
            load_rules(ROOT/'tools/kimodo_pipeline/directional_gait_rules.json'))

    def test_sideways_travel_is_lateral_and_not_diagonal(self):
        for gait in ('Walk', 'Run', 'Crouch_Walk'):
            for variant, heading in (('Forward', 45), ('Backward', -45)):
                name = gait+'_Left_'+variant
                if name in self.graph['rules']['programmaticGaits']:
                    derived=self.graph['nodes']['motion:'+name]
                    self.assertEqual('mirror',derived['op'])
                    self.assertTrue(derived['params']['reverseTime'])
                    self.assertEqual('motion:'+name.replace('Backward','Forward'),derived['params']['source'])
                else:
                    candidate = self.graph['nodes']['candidate:'+name+':discovery']
                    path = candidate['params']['rootPath']
                    self.assertEqual([1, 0], path['direction'])
                    self.assertEqual(heading, path['headingDegrees'])
                    self.assertEqual([], candidate['params']['anchors'])
                    self.assertEqual('linear', path['kind'])
                    self.assertEqual('gait_cycle', self.graph['nodes']['motion:'+name]['op'])
                right = gait+'_Right_'+variant
                self.assertEqual('mirror', self.graph['nodes']['motion:'+right]['op'])
                self.assertNotIn('candidate:'+right, self.graph['nodes'])

    def test_native_sprint_impulse_is_a_loop_without_standing_start(self):
        node = self.graph['nodes']['candidate:Sprint_Acceleration:discovery']
        self.assertEqual('linear', node['params']['rootPath']['kind'])
        self.assertTrue(all(a['source'] != 'motion:Stand_Pose' for a in node['params']['anchors']))
        self.assertIn('motion:Run_Forward', node['deps'])
        self.assertEqual('gait_cycle', self.graph['nodes']['motion:Sprint_Acceleration']['op'])
        order = topological_order(self.graph['nodes'], ['motion:Sprint_Acceleration'])
        self.assertLess(order.index('motion:Run_Forward'), order.index('candidate:Sprint_Acceleration'))
        self.assertEqual(5,node['params']['rootPath']['velocity'])
        candidate=self.graph['nodes']['candidate:Sprint_Acceleration']
        self.assertEqual(3,candidate['params']['fullBodyPhaseCycles'])
        self.assertEqual('native_gait_reference',self.graph['nodes'][candidate['params']['gaitReference']]['op'])
        self.assertEqual('periodic_repair',self.graph['nodes']['repair:Sprint_Acceleration:generated']['op'])

    def test_constant_heading_is_applied_to_dense_discovery_path(self):
        source, rig = walking_fixture()
        p = dict(seconds=2, fps=60, anchors=[], rootPath=dict(
            kind='linear', direction=[1, 0], velocity=.6, headingDegrees=45))
        constraint = materialize_constraints(p, lambda _: source, rig)[0]
        path = np.asarray(constraint['smooth_root_2d'])
        np.testing.assert_allclose(np.diff(path, axis=0), np.tile([.01, 0], (119, 1)), atol=1e-12)
        np.testing.assert_allclose(constraint['global_root_heading'],
            np.tile([np.sqrt(.5), np.sqrt(.5)], (120, 1)), atol=1e-12)

    def test_non_integral_final_frame_keeps_its_own_cycle_phase(self):
        source, rig = walking_fixture(seconds=5)
        p = dict(seconds=4.2, fps=60, targetPeriodSeconds=1, constrainClipEndpoint=True,
            rootPath=dict(kind='linear', direction=[0, 1], velocity=.6, headingDegrees=0))
        constraints, receipt = materialize_gait_constraints(p, source, rig)
        body = next(c for c in constraints if c['type']=='fullbody')
        self.assertIn(251, body['frame_indices'])
        a = body['frame_indices'].index(251); b = body['frame_indices'].index(0)
        self.assertGreater(np.linalg.norm(np.asarray(body['local_joints_rot'][a])-
                                         np.asarray(body['local_joints_rot'][b])), .01)
        self.assertTrue(receipt['pass'])

    def test_slower_cadence_keeps_world_stance_foot_fixed(self):
        from .test_gait_constraints import decode
        source, rig = walking_fixture(seconds=5)
        p = dict(seconds=4, fps=60, targetPeriodSeconds=1.2,
            matchStrideToPeriod=True, fullBodyPhaseFractions=[.25, .5, .75],
            rootPath=dict(kind='linear', direction=[0, 1], velocity=.6, headingDegrees=0))
        constraints, receipt = materialize_gait_constraints(p, source, rig)
        poses = decode(next(c for c in constraints if c['type']=='fullbody'), rig)
        foot = rig['jointNames'].index('LeftFoot')
        # The slower cycle needs a longer stride. Copying unscaled poses would
        # move this stance ankle in world space even though the root path fits.
        np.testing.assert_allclose(poses[18][3][foot], poses[36][3][foot], atol=1e-10)
        self.assertGreater(poses[36][1][2]-poses[18][1][2], .17)
        self.assertTrue(receipt['strideMatchedToPeriod'])


class MirroredGaitTests(unittest.TestCase):
    def test_saved_accepted_master_remains_valid_for_mirror(self):
        source,rig=walking_fixture(seconds=5)
        master,receipt=author_cycle(source,rig,61,.6)
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'master.npz';save_motion(path,master,rig)
            loaded=load_motion(path,rig)
        np.testing.assert_array_equal(master['local_rot_mats'],loaded['local_rot_mats'])
        _,checked=mirror_authored_cycle(loaded,freeze(source,0,1,rig),rig,receipt)
        self.assertTrue(checked['pass'])

    def test_reversed_mirror_remeasures_real_backward_support(self):
        source,rig=walking_fixture(seconds=5)
        master,receipt=author_cycle(source,rig,61,.6)
        reflected,checked=mirror_authored_cycle(master,freeze(source,0,1,rig),rig,receipt,reverse_time=True)
        self.assertTrue(checked['pass'])
        self.assertTrue(checked['mirrorProvenance']['reversedTime'])
        self.assertLess(checked['removedPlanarTrajectory']['cycleDisplacement'][2],0)

    def test_mirror_remeasures_support_and_preserves_cycle_travel(self):
        source, rig = walking_fixture(seconds=5)
        master, receipt = author_cycle(source, rig, 61, .6)
        reflected, checked = mirror_authored_cycle(master, freeze(source, 0, 1, rig), rig, receipt)
        self.assertTrue(checked['pass'])
        self.assertIn('mirrorProvenance', checked)
        self.assertEqual(61, checked['outputFrameCount'])
        self.assertEqual('right', checked['events'][0]['side'])
        self.assertEqual('right', checked['cycleAnchorSide'])
        # This symmetric fixture permits exact comparison at unchanged time
        # indices. Re-cropping at the opposite landing would violate it.
        from .motion import mirror
        neutral = complete(np.broadcast_to(np.eye(3), (1, len(rig['jointNames']), 3, 3)).copy(),
                           np.zeros((1, 3)), rig, 60.)
        expected = mirror(master, neutral, rig)
        np.testing.assert_allclose(reflected['local_rot_mats'], expected['local_rot_mats'], atol=1e-10)
        self.assertAlmostEqual(.6, checked['actualAuthoredSpeedMps'], places=8)
        self.assertLess(np.max(np.abs(reflected['root_positions'][:, [0, 2]])), .01)

    def test_mirror_rejects_unvalidated_or_modified_master(self):
        source, rig = walking_fixture(seconds=5)
        master, receipt = author_cycle(source, rig, 61, .6)
        master['root_positions'][5, 0] += .01
        with self.assertRaisesRegex(ValueError, 'unchanged accepted'):
            mirror_authored_cycle(master, freeze(source, 0, 1, rig), rig, receipt)


if __name__ == '__main__':
    unittest.main()

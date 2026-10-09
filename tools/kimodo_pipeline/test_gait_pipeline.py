"""Gait gates must own dependency execution and final asset provenance."""
import json
from pathlib import Path
import tempfile
import unittest

from .graph import build_graph, digest, load_rules, read_json, topological_order
from .runner import Runner
from .motion import complete, save_motion
from .test_gait import walking_fixture


ROOT = Path(__file__).resolve().parents[2]


class GaitGraphTests(unittest.TestCase):
    def test_repair_receipt_precedes_regeneration_in_feedback_profile(self):
        contracts = read_json(ROOT / 'docs/architecture/kimodo-als-asset-contracts.json')
        graph = build_graph(contracts, load_rules(ROOT / 'tools/kimodo_pipeline/gait_repair_feedback_rules.json'))
        reference = 'reference:Walk_Forward:repaired'
        generated = graph['nodes']['candidate:Walk_Forward']
        self.assertEqual('periodic_repair', graph['nodes'][reference]['op'])
        self.assertIn(reference, generated['deps'])
        self.assertEqual(reference, generated['params']['gaitReference'])
        order = topological_order(graph['nodes'], ['asset:refactored:A_Als_Walk_Forward'])
        self.assertLess(order.index(reference), order.index('candidate:Walk_Forward'))

    def test_refinement_and_quality_gate_precede_pose_dependents(self):
        contracts = read_json(ROOT / 'docs/architecture/kimodo-als-asset-contracts.json')
        before = digest(contracts)
        graph = build_graph(contracts, load_rules(ROOT / 'tools/kimodo_pipeline/gait_closure_rules.json'))
        for name in ['Walk_Forward', 'Run_Forward', 'Crouch_Walk_Forward']:
            candidate, discovery, master = 'candidate:' + name, 'candidate:' + name + ':discovery', 'motion:' + name
            self.assertIn(discovery, graph['nodes'][candidate]['deps'])
            self.assertEqual(discovery, graph['nodes'][candidate]['params']['gaitReference'])
            self.assertEqual('gait_cycle', graph['nodes'][master]['op'])
        order = topological_order(graph['nodes'], ['motion:Walk_Forward_Pose'])
        self.assertLess(order.index('motion:Walk_Forward'), order.index('motion:Walk_Forward_Pose'))
        self.assertEqual(before, digest(contracts), 'Building a new graph must preserve original source contracts')

    def test_rules_inheritance_cycle_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'a.json').write_text('{"extends":"b.json"}')
            (path / 'b.json').write_text('{"extends":"a.json"}')
            with self.assertRaisesRegex(ValueError, 'inheritance cycle'):
                load_rules(path / 'a.json')


class GaitRunnerTests(unittest.TestCase):
    def test_bad_realization_stops_descendants_and_preserves_failure_receipt(self):
        motion, rig = walking_fixture()
        # A stationary pose translated along the path has no real steps and slides.
        local = motion['local_rot_mats'].copy()
        local[:] = local[0]
        broken = complete(local, motion['root_positions'], rig, float(motion['fps']))
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'rig.json').write_text(json.dumps(rig))
            save_motion(root / 'source.npz', broken, rig)
            nodes = {
                'rig:soma': dict(id='rig:soma', op='rig', deps=[], params={}),
                'candidate': dict(id='candidate', op='generate', deps=['rig:soma'], params={}),
                'master': dict(id='master', op='gait_cycle', deps=['rig:soma', 'candidate'],
                               params=dict(source='candidate', targetCount=61, expectedSpeedMps=.6, policy={})),
                'pose': dict(id='pose', op='freeze', deps=['master'], params=dict(source='master', frame=0, count=1)),
            }
            rules = dict(fps=60)
            graph = dict(nodes=nodes, order=topological_order(nodes), rules=rules, rulesDigest=digest(rules))
            runner = Runner(graph, root / 'output', bindings={
                'rig:soma': {'path': str(root / 'rig.json')},
                'candidate': {'path': str(root / 'source.npz')}})
            with self.assertRaisesRegex(ValueError, 'Gait authoring rejected'):
                runner.run(['pose'])
            self.assertNotIn('pose', runner.results)
            failures = list((root / 'output').glob('.building-*/gait-validation.json'))
            self.assertEqual(1, len(failures))
            self.assertFalse(read_json(failures[0])['pass'])
            self.assertFalse((root / 'output/running.lock').exists())


if __name__ == '__main__':
    unittest.main()

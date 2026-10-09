"""Independent branch continuation must never bypass an authoring gate."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from .batch import run_batch
from .graph import read_json


class FakeRunner:
    def __init__(self, output):
        self.output = Path(output)
        self.graph = {'nodes':{key:dict(op='deliver', deps=deps) for key,deps in
            [('gate',[]),('blocked',['gate']),('independent',[])]}}
        self.results = {}
        self.events = []
        self.visited = []

    def execute_node(self, key):
        self.visited.append(key)
        if key == 'gate':
            self.events.append(dict(status='failed', evidence='failed-gate-evidence'))
            raise ValueError('Foot slide rejected')
        self.events.append(dict(status='built'))
        self.results[key] = dict(identity=key, op='deliver', boundInput=False)


class BatchTests(unittest.TestCase):
    def test_continue_independent_branch_and_preserve_failed_dependency(self):
        with tempfile.TemporaryDirectory() as directory, patch('tools.kimodo_pipeline.viewer_export.export_batch', return_value=None) as export:
            runner = FakeRunner(directory)
            result = run_batch(runner, ['blocked','independent'])
            self.assertEqual(runner.visited, ['gate','independent'])
            self.assertEqual(result['completedTargets'], 1)
            self.assertEqual(result['failedNodes'], 1)
            self.assertEqual(result['blockedNodes'], 1)
            self.assertEqual(result['status'], 'authoring_rejected')
            self.assertFalse(result['productionReady'])
            self.assertEqual(result['nodes']['blocked']['dependencies'], ['gate'])
            self.assertIn('Foot slide', read_json(Path(directory)/'batch-result.json')['nodes']['gate']['error'])
            self.assertFalse((Path(directory)/'running.lock').exists())
            export.assert_called_once()

    def test_lock_never_overwrites_another_running_batch(self):
        with tempfile.TemporaryDirectory() as directory:
            lock = Path(directory)/'running.lock'
            lock.write_text('existing-owner')
            with self.assertRaises(FileExistsError):
                run_batch(FakeRunner(directory), ['independent'])
            self.assertEqual(lock.read_text(), 'existing-owner')


if __name__ == '__main__':
    unittest.main()

"""Safety and candidate-selection checks for the resource scan tool."""
import json
from pathlib import Path
import tempfile
import unittest

from tools.godot_resource_scan import configure, MARKER


class ResourceScanTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.generated = self.root / 'assets/generated'
        for name in ('als_v4', 'als_v4_raw', 'lyra_als', 'active', 'candidate', 'history'):
            directory = self.generated / name
            directory.mkdir(parents=True)
            (directory / 'resource-pack.json').write_text('{}')
        pointer = self.root / 'assets/config/als_resource_pack.json'
        pointer.parent.mkdir()
        pointer.write_text(json.dumps(dict(pack='res://assets/generated/active/resource-pack.json')))

    def test_scope_preserves_payload_and_is_repeatable(self):
        before = {path: path.read_bytes() for path in self.generated.rglob('*.json')}
        preview = configure(self.root)
        self.assertFalse(list(self.generated.rglob('.gdignore')))
        self.assertEqual(set(preview['visible']), {'als_v4', 'als_v4_raw', 'lyra_als', 'active'})
        configure(self.root, apply=True)
        self.assertEqual((self.generated / 'history/.gdignore').read_bytes(), MARKER)
        self.assertEqual(configure(self.root, apply=True)['changes'], [])
        self.assertEqual(before, {path: path.read_bytes() for path in before})

    def test_candidate_reopened_and_retired(self):
        configure(self.root, apply=True)
        report = configure(self.root, includes=['res://assets/generated/candidate/resource-pack.json'], apply=True)
        self.assertIn('candidate', report['visible'])
        self.assertFalse((self.generated / 'candidate/.gdignore').exists())
        configure(self.root, apply=True)
        self.assertTrue((self.generated / 'candidate/.gdignore').exists())

    def test_unsafe_or_missing_selection_fails_before_mutation(self):
        for value in ('res://assets/generated/../../outside', 'res://assets/generated/missing'):
            with self.assertRaises(ValueError):
                configure(self.root, includes=[value], apply=True)
        self.assertFalse(list(self.generated.rglob('.gdignore')))

    def test_custom_marker_preserved(self):
        marker = self.generated / 'candidate/.gdignore'
        marker.write_bytes(b'custom policy')
        with self.assertRaises(ValueError):
            configure(self.root, includes=[str(marker.parent)], apply=True)
        self.assertEqual(marker.read_bytes(), b'custom policy')
        self.assertFalse((self.generated / 'history/.gdignore').exists())


if __name__ == '__main__':
    unittest.main()

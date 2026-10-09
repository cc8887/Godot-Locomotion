"""Cache provenance checks; these fixtures are never generation embeddings."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np

from .text_embeddings import FrozenTextEmbeddings


class FrozenEmbeddingTests(unittest.TestCase):
    def make_cache(self, root):
        array = root / 'fixture.npy'
        np.save(array, np.zeros((1, 4096), dtype=np.float32))
        manifest = dict(schemaVersion=1, checkpointIdentity='original-checkpoints',
                        nativePeftMergeEquivalence=True, prompts={'test fixture': dict(
                            file=array.name, sha256=hashlib.sha256(array.read_bytes()).hexdigest())})
        path = root / 'manifest.json'
        path.write_text(json.dumps(manifest))
        return path, array

    def test_checkpoint_change_cannot_reuse_embedding(self):
        with tempfile.TemporaryDirectory() as directory:
            path, _ = self.make_cache(Path(directory))
            with self.assertRaisesRegex(ValueError, 'different model/encoder'):
                FrozenTextEmbeddings(path, 'changed-checkpoints')

    def test_payload_change_cannot_reuse_embedding(self):
        with tempfile.TemporaryDirectory() as directory:
            path, array = self.make_cache(Path(directory))
            np.save(array, np.ones((1, 4096), dtype=np.float32))
            with self.assertRaisesRegex(ValueError, 'Changed text embedding'):
                FrozenTextEmbeddings(path, 'original-checkpoints')

    def test_missing_native_equivalence_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path, _ = self.make_cache(Path(directory))
            manifest = json.loads(path.read_text())
            manifest['nativePeftMergeEquivalence'] = False
            path.write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, 'equivalence evidence'):
                FrozenTextEmbeddings(path, 'original-checkpoints')


if __name__ == '__main__':
    unittest.main()

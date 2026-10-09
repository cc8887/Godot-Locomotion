"""Validated, cache-only embeddings from the same native Kimodo text encoder.

This adapter avoids keeping the full text model resident during motion inference.
It never falls back to another encoder, a network service or a missing prompt.
"""
from pathlib import Path
import hashlib
import json

import numpy as np


class FrozenTextEmbeddings:
    def __init__(self, path, checkpoint_identity):
        self.path = Path(path).resolve()
        self.manifest = json.loads(self.path.read_text(encoding='utf-8'))
        if self.manifest.get('schemaVersion') != 1 or self.manifest.get('checkpointIdentity') != checkpoint_identity:
            raise ValueError('Text embedding cache belongs to different model/encoder checkpoints')
        if self.manifest.get('nativePeftMergeEquivalence') is not True:
            raise ValueError('Text embedding cache lacks native encoder equivalence evidence')
        self.arrays = {}
        for text, entry in self.manifest['prompts'].items():
            file = (self.path.parent / entry['file']).resolve()
            if not file.is_relative_to(self.path.parent):
                raise ValueError('Embedding payload leaves the cache directory')
            if hashlib.sha256(file.read_bytes()).hexdigest() != entry['sha256']:
                raise ValueError('Changed text embedding payload')
            array = np.load(file, allow_pickle=False)
            if array.shape != (1, 4096) or array.dtype != np.float32 or not np.isfinite(array).all():
                raise ValueError('Invalid native text embedding')
            self.arrays[text] = array

    def __call__(self, texts):
        import torch
        from kimodo.sanitize import sanitize_texts
        if isinstance(texts, str):
            texts = [texts]
        texts = sanitize_texts(list(texts))
        missing = [text for text in texts if text not in self.arrays]
        if missing:
            raise ValueError('Exact prompt is missing from the frozen native embedding cache: ' + repr(missing))
        return torch.from_numpy(np.stack([self.arrays[text] for text in texts])), [1] * len(texts)

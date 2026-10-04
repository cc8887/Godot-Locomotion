"""Check dependency resolution from a different working directory."""
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import locomotion_paths as paths


class PortablePathsTests(unittest.TestCase):
    def test_defaults_do_not_depend_on_working_directory(self):
        previous = Path.cwd()
        with tempfile.TemporaryDirectory() as foreign, patch.dict(os.environ, {}, clear=True):
            try:
                os.chdir(foreign)
                self.assertEqual(paths.project_path('Content'), paths.REPOSITORY_ROOT.parent / 'GASP58/Content')
                self.assertEqual(paths.engine_path('Engine'), paths.REPOSITORY_ROOT.parent / 'UE_5.8/Engine')
                self.assertEqual(paths.unreal_source_path(), paths.REPOSITORY_ROOT.parent / 'UnrealEngine')
            finally:
                os.chdir(previous)

    def test_relative_override_with_spaces(self):
        with patch.dict(os.environ, {'LYRA_UE_PROJECT_FILE': '../Source Project/Source.uproject',
                                     'UE_ENGINE_ROOT': '../Engine Install'}, clear=True):
            self.assertEqual(paths.project_path('Content'), paths.REPOSITORY_ROOT.parent / 'Source Project/Content')
            self.assertEqual(paths.engine_path(), paths.REPOSITORY_ROOT.parent / 'Engine Install')

    def test_absolute_local_override(self):
        with tempfile.TemporaryDirectory() as dependency, patch.dict(
                os.environ, {'UE_ENGINE_ROOT': dependency}, clear=True):
            self.assertEqual(paths.engine_path('Engine'), Path(dependency).resolve() / 'Engine')


if __name__ == '__main__':
    unittest.main()

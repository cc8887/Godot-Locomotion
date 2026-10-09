"""Provenance regressions: don't display a legacy pack as the active one."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('motion_viewer_server', Path(__file__).with_name('server.py'))
server = importlib.util.module_from_spec(spec)
spec.loader.exec_module(server)


class ActivePackTests(unittest.TestCase):
    def test_batches_select_exact_run_and_do_not_fallback_to_legacy_files(self):
        import hashlib
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);preview=root/'artifacts/kimodo_viewer';preview.mkdir(parents=True)
            entries=[]
            for batch in ('new','old'):
                folder=preview/batch;folder.mkdir()
                motion=folder/'run.motion.json';motion.write_text('{}',encoding='utf-8')
                index=folder/'index.json';index.write_text(json.dumps(dict(id=batch,label=batch,status='failed',
                    entries=[dict(name='ALS_N_Run_LF',path=batch+'/run.motion.json',phase='generated',kind='source-motion',
                                  size=2,sidecars=[],url='/authoring-file/'+batch+'/run.motion.json')])),encoding='utf-8')
                entries.append(dict(id=batch,index=batch+'/index.json',sha256=hashlib.sha256(index.read_bytes()).hexdigest()))
            (preview/'current.json').write_text(json.dumps(dict(latestBatch='new',batches=entries)),encoding='utf-8')
            with patch.object(server,'PROJECT_ROOT',str(root)):
                stages,_=server.stage_catalog()
                files=server.list_stage_files(stages[0]);self.assertEqual('new/run.motion.json',files[0]['path'])
                old,_=server.stage_catalog('old');self.assertEqual('old/run.motion.json',old[0]['files'][0]['path'])
                chain=server.build_chain_index(stages)
                run=next(item for item in chain if item['key']=='ALS_N_Run_LF')
                self.assertEqual('new/run.motion.json',run['stages']['authoring-generated']['path'])
                selected=server.chain_catalog()
                selected_run=next(i for i in selected['items'] if i['key']=='ALS_N_Run_LF')
                self.assertEqual('new/run.motion.json',selected_run['stages']['authoring-generated']['path'])
                self.assertNotIn('kimodo-bvh',selected['chainStages'])
                self.assertNotIn('authoring-target-root',selected_run['stages'])
                compared=server.chain_catalog(view='batches',compare='old')
                pair=next(i for i in compared['items'] if i['key']=='ALS_N_Run_LF')
                self.assertEqual('new/run.motion.json',pair['stages']['batch-0']['path'])
                self.assertEqual('old/run.motion.json',pair['stages']['batch-1']['path'])
                with self.assertRaisesRegex(ValueError,'Unknown authoring batch'):
                    server.chain_catalog(view='batches',compare='missing')
                with self.assertRaisesRegex(ValueError,'Unknown authoring batch'):server.stage_catalog('missing')
                (preview/'new/index.json').write_text('{}',encoding='utf-8')
                with self.assertRaisesRegex(ValueError,'index changed'):server.stage_catalog()

    def test_authoring_mount_cannot_expose_other_project_files(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(server,'PROJECT_ROOT',directory):
                self.assertIsNone(server.resolve_authoring_path('../../assets/config/als_resource_pack.json'))
                self.assertIsNone(server.resolve_authoring_path('..%2f..%2fsecret.motion.json'))
                self.assertIsNone(server.resolve_authoring_path('..\\..\\secret.motion.json'))
                self.assertIsNotNone(server.resolve_authoring_path('batches/run/source.motion.json'))
    def test_active_pointer_and_invalid_pointer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            generated = root/'assets/generated'
            (root/'assets/config').mkdir(parents=True)
            (generated/'candidate/v4/animations').mkdir(parents=True)
            (generated/'candidate/raw/raw_sequences').mkdir(parents=True)
            (generated/'candidate/resource-pack.json').write_text(json.dumps(dict(
                assetRoot='res://assets/generated/candidate/v4',
                rawRoot='res://assets/generated/candidate/raw')), encoding='utf8')
            pointer = root/'assets/config/als_resource_pack.json'
            pointer.write_text(json.dumps(dict(pack='res://assets/generated/candidate/resource-pack.json')), encoding='utf8')
            with patch.object(server, 'PROJECT_ROOT', str(root)), patch.object(server, 'ASSETS_ROOT', os.path.realpath(generated)):
                stages, provenance = server.stage_catalog()
                final = next(s for s in stages if s['id'] == 'kimodo-godot')
                self.assertEqual(final['dirs'][0]['rel'], 'candidate/v4/animations')
                self.assertEqual(provenance['assetRoot'], 'candidate/v4')
                pointer.write_text(json.dumps(dict(pack='res://assets/generated/../../secret.json')), encoding='utf8')
                with self.assertRaises(ValueError):
                    server.stage_catalog()
                pointer.unlink()
                stages, provenance = server.stage_catalog()
                self.assertIsNone(provenance)
                self.assertEqual(next(s for s in stages if s['id'] == 'kimodo-godot')['dirs'][0]['rel'], 'als_v4/animations')


if __name__ == '__main__':
    unittest.main()

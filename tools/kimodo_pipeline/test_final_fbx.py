"""Native round-trip receipts must bind the exact pending pack and its full poses."""
import tempfile
import unittest
from pathlib import Path
from .publish import ROOT, file_hash, write_json, read_json, finalize_fbx
from .root_ownership import POLICY


class FinalFbxTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='final-fbx-',dir=ROOT/'artifacts/tests')
        self.addCleanup(self.temp.cleanup)
        self.folder=Path(self.temp.name);self.pack=self.folder/'resource-pack.json'
        self.raw=self.folder/'root-ownership/walk/runtime.raw.json';self.raw.parent.mkdir(parents=True)
        self.timing=dict(sampledKeyCount=2,playLength=1/30,frameRateNumerator=30,frameRateDenominator=1)
        write_json(self.raw,dict(**self.timing,tracks=[]))
        self.fbx=self.folder/'v4/animations/Walk.fbx';self.fbx.parent.mkdir(parents=True)
        self.fbx.write_bytes(b'intermediate-before-final-bake')
        self.manifest=self.folder/'v4/als_manifest.json'
        write_json(self.manifest,dict(animations=[dict(objectPath='Walk',outputPath='animations/Walk.fbx',
            kimodoSource=dict(pipeline='kimodo-als-final',fbxSha256=file_hash(self.fbx)))],
            files=[dict(relativePath='animations/Walk.fbx',sha256=file_hash(self.fbx),size=self.fbx.stat().st_size)]))
        self.resource='res://'+self.pack.relative_to(ROOT).as_posix()
        self.request=self.folder/'final-fbx-request.json'
        write_json(self.request,dict(pack=self.resource,jobs=[dict(source='Walk',raw=str(self.raw),
            rawSha256=file_hash(self.raw),fbx=str(self.fbx),sourceAsset='/Game/Scratch/Walk')]))
        write_json(self.pack,dict(rootOwnershipPolicy=POLICY,finalFbxStatus='pending-native-bake',files=[
            dict(path='res://'+p.relative_to(ROOT).as_posix(),sha256=file_hash(p))
            for p in (self.request,self.raw,self.manifest,self.fbx)]))
        self.fbx.write_bytes(b'final-full-pose-fbx')
        self.receipt=self.folder/'native-receipt.json'
        self.saved=dict(source='Walk',raw=str(self.raw),rawSha256=file_hash(self.raw),fbx=str(self.fbx),
            fbxSha256=file_hash(self.fbx),passed=True,timeDomainMatches=True,sampledKeyCount=2,
            maximumPositionErrorCm=.001,maximumRotationErrorDegrees=.01,maximumScaleError=0,
            beforeExportMaximumErrors=[.001,.01,0],readbackPolicy='native-DataModel-physical-tracks',
            authoredTiming=self.timing.copy(),readbackTiming=self.timing.copy())
        self.save_receipt()

    def save_receipt(self):
        write_json(self.receipt,dict(requestSha256=file_hash(self.request),jobs=[self.saved]))

    def test_accepts_full_pose_readback_and_refreshes_fbx_and_manifest_hashes(self):
        finalize_fbx(self.pack,self.receipt)
        pack=read_json(self.pack)
        self.assertEqual(pack['finalFbxStatus'],'native-bake-and-readback-verified')
        for row in pack['files']:self.assertEqual(row['sha256'],file_hash(ROOT/row['path'].removeprefix('res://')))
        manifest=read_json(self.manifest)
        self.assertEqual(manifest['animations'][0]['kimodoSource']['fbxSha256'],file_hash(self.fbx))
        with self.assertRaisesRegex(ValueError,'pending'):finalize_fbx(self.pack,self.receipt)

    def test_wrong_key_domain_or_lossy_pose_does_not_refresh_any_pack_bytes(self):
        before=self.pack.read_bytes();manifest_before=self.manifest.read_bytes()
        for field,value in (('timeDomainMatches',False),('sampledKeyCount',3),
                            ('maximumScaleError',.001),('maximumPositionErrorCm',.02),
                            ('maximumRotationErrorDegrees',.2)):
            original=self.saved[field];self.saved[field]=value;self.save_receipt()
            with self.assertRaisesRegex(ValueError,'readback failed'):finalize_fbx(self.pack,self.receipt)
            self.assertEqual(before,self.pack.read_bytes());self.assertEqual(manifest_before,self.manifest.read_bytes())
            self.saved[field]=original

    def test_rebaked_fbx_does_not_allow_unrelated_staged_input_changes(self):
        write_json(self.raw,dict(sampledKeyCount=2,tracks=[dict(bone='changed')]))
        self.saved['rawSha256']=file_hash(self.raw);self.save_receipt()
        with self.assertRaisesRegex(ValueError,'Changed staged input'):finalize_fbx(self.pack,self.receipt)

    def test_mutating_request_or_fbx_invalidates_receipt(self):
        self.fbx.write_bytes(b'another-fbx-after-readback')
        with self.assertRaisesRegex(ValueError,'inputs or output changed'):finalize_fbx(self.pack,self.receipt)

    def test_forged_empty_closure_cannot_certify_unbaked_manifest_animation(self):
        request=read_json(self.request);request['jobs']=[];write_json(self.request,request)
        pack=read_json(self.pack)
        for row in pack['files']:
            if row['path'].endswith('final-fbx-request.json'):row['sha256']=file_hash(self.request)
        write_json(self.pack,pack)
        write_json(self.receipt,dict(requestSha256=file_hash(self.request),jobs=[]))
        with self.assertRaisesRegex(ValueError,'closure differs'):finalize_fbx(self.pack,self.receipt)

    def test_true_status_does_not_override_a_changed_native_frame_rate(self):
        self.saved['readbackTiming']['frameRateNumerator']=60;self.save_receipt()
        with self.assertRaisesRegex(ValueError,'readback failed'):finalize_fbx(self.pack,self.receipt)


if __name__=='__main__':unittest.main()

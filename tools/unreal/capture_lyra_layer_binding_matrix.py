"""Owned native binding capture; restores in-process metadata, saves no assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/lyra-analysis'
TAG = os.environ['LYRA_LAYER_BINDING_TAG']
assert TAG.replace('-', '').isalnum()
PACKAGE = Path(os.environ['LYRA_LAYER_BINDING_PACKAGE'])
REQUEST = OUT / f'{TAG}-request.json'
OUTPUT = OUT / f'{TAG}-native.json'
CLOSURE = OUT / f'{TAG}-closure.json'
assert not OUTPUT.exists() and not CLOSURE.exists()
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
assets = ROOT / 'assets/generated/lyra_als'
project = Path(unreal.Paths.get_project_file_path()).parent
protected = {str(p.relative_to(assets)).replace('\\', '/'): sha(p) for p in assets.rglob('*.json')}
configuration = {str(p.relative_to(project)).replace('\\', '/'): sha(p) for p in
                 [project / 'GASP58.uproject', *(project / 'Config').rglob('*.ini')]}
packages = json.loads((assets / 'named_notify_v1_policy.json').read_bytes())['assetSha256']
contracts = json.loads((assets / 'linked_layer_contracts.json').read_bytes())
packages.update({contracts['classes'][name]['class']: digest for name, digest in contracts['assetSha256'].items()})
def original(path):
    path = path.split('.')[0]
    if path.startswith('/Game/'):
        return project / 'Content' / (path.removeprefix('/Game/') + '.uasset')
    if path.startswith('/ShooterCore/'):
        return project / 'Plugins/GameFeatures/ShooterCore/Content' / (path.removeprefix('/ShooterCore/') + '.uasset')
    raise ValueError(path)
def protect():
    for name, digest in protected.items(): assert sha(assets / name) == digest, name
    for name, digest in configuration.items(): assert sha(project / name) == digest, name
    for name, digest in packages.items(): assert sha(original(name)) == digest, name

source = ROOT / 'tools/unreal/LyraWholeMainOracle'
source_sha = {str(p.relative_to(source)).replace('\\', '/'): sha(p) for p in (source / 'Source').rglob('*') if p.is_file()}
for name, digest in source_sha.items(): assert sha(PACKAGE / name) == digest, name
protect()
request_bytes = REQUEST.read_bytes()
try:
    value = unreal.LyraWholeMainOracleLibrary.read_layer_binding_matrix(request_bytes.decode('utf-8'))
    if not value: raise RuntimeError('Original UE binding probe returned no result')
    native = json.loads(value)
    assert native['metadataRestored'] and not native['posesEvaluated']
    assert len(native['cases']) == 10
    with OUTPUT.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(native, separators=(',', ':'), allow_nan=False) + '\n')
finally:
    protect()
closure = dict(requestSha256=sha(REQUEST), nativeSha256=sha(OUTPUT), protectedJson=protected,
               configuration=configuration, assetSha256=packages, probeSourceSha256=source_sha,
               scriptSha256=sha(Path(__file__)), metadataRestored=True, assetsSaved=0,
               operators=['UAnimInstance.LinkAnimClassLayers', 'UAnimInstance.UnlinkAnimClassLayers'])
with CLOSURE.open('x', encoding='utf-8', newline='\n') as stream:
    stream.write(json.dumps(closure, separators=(',', ':')) + '\n')
unreal.log('LYRA_LAYER_BINDING_MATRIX_NATIVE_OK cases=10 steps=' +
           str(sum(len(c['steps']) for c in native['cases'])) + ' assets_saved=0 metadata_restored=true')

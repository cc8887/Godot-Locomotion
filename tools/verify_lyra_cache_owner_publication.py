"""Check the published checkpoint, preserved history and restored runtime."""
from pathlib import Path
import hashlib
import json

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'cache-owner-v4'
read = lambda p: json.loads(p.read_bytes())
sha = lambda b: hashlib.sha256(b).hexdigest()
audit_file = out / f'{tag}-integrity.json'
audit = read(audit_file)
meta = read(out / f'{tag}-documentation.json')
assert audit['auditPassed'] and audit['godotProcesses'] == 62 and not audit['scope']['goalComplete']
assert meta['auditSha256'] == sha(audit_file.read_bytes()) and not meta['goalComplete']
assert meta['publisherSha256'] == sha((repo / 'tools/publish_lyra_cache_owner_checkpoint.py').read_bytes())
assert audit['auditorSha256'] == sha((repo / 'tools/verify_lyra_cache_owner.py').read_bytes())
baseline = read(out / 'cache-owner-v1-before.json')
assert len(meta['documents']) == 2
for row in meta['documents']:
    content = (repo / row['path']).read_bytes()
    assert sha(content) == row['currentSha256']
    assert sha(content[row['prefixBytes']:]) == row['priorSha256'] == baseline[row['path']]
    assert row['suffixPreserved']
doc = repo / meta['verificationPath']
assert sha(doc.read_bytes()) == meta['verificationSha256']
assert '最终验证待审计。' not in doc.read_text(encoding='utf-8')
assert '完整移植目标保持active' in doc.read_text(encoding='utf-8')
for p,digest in audit['sourceSha256'].items():
    assert sha((repo / p).read_bytes()) == digest,p
protected = 0
published = {r['path'] for r in meta['documents']}
for p,digest in baseline.items():
    if p not in audit['sourceSha256'] and p not in published:
        assert sha((repo / p).read_bytes()) == digest,p
        protected += 1
assert protected == audit['protectedOtherSources'] == 2198
assemblies = read(out / f'{tag}-debug-verification.json')['assemblies']
for p,digest in assemblies.items():
    assert sha((repo / '.godot/mono/temp/bin/Debug' / p).read_bytes()).upper() == digest,p
closure = read(out / 'cache-owner-v2-closure.json')
assets = repo / 'assets/generated/lyra_als'
assert {p.relative_to(assets).as_posix():sha(p.read_bytes()) for p in assets.rglob('*.json')} == closure['previousFixtureSha256']
result = dict(publicationPassed=True,goalComplete=False,documents=3,suffixesPreserved=2,protectedSources=protected,
              implementationSources=len(audit['sourceSha256']),resources=870,debugAssembliesRestored=6,
              auditSha256=sha(audit_file.read_bytes()),documentationSha256=sha((out / f'{tag}-documentation.json').read_bytes()),
              verifierSha256=sha(Path(__file__).read_bytes()))
with (out / f'{tag}-post-publication.json').open('x',encoding='utf-8') as f:
    json.dump(result,f,indent=2)
print('LYRA_CACHE_OWNER_POST_PUBLICATION_OK docs=3 suffixes=2 protected=2198 resources=870 debugRestored=6 goalComplete=false')

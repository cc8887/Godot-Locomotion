"""Capture linked-instance dispatch through the copied component dispatch path."""
import copy
import hashlib
import json
from pathlib import Path

repo_root = Path(__file__).resolve().parents[2]
original = repo_root / 'tools/unreal/capture_lyra_linked_private_v2.py'
original_base = repo_root / 'tools/unreal/capture_lyra_whole_main.py'
raw = original_base.read_bytes()
base = raw.decode('utf-8')
anchor = "    save('request',requests)"
montage = "montage_catalog=load('montage_catalog_v2.json') if case in ('actions','rebind') else None"
source = "    source=repo/'tools/unreal/LyraWholeMainOracle'"
assert base.count(anchor) == base.count(montage) == base.count(source) == 1


def author_events(request):
    request['traces'] = [t for t in request['traces'] if t['layout'] in ('single', 'per-call')]
    assert len(request['traces']) == 6
    for trace in request['traces']:
        for i, frame in enumerate(trace['frames']):
            # Exercise ordinary component dispatch, then the manual Main-only
            # path, and resume component dispatch while the body graph is hidden.
            time = i / trace['hz']
            frame['dispatchLinked'] = not 4 <= time < 5
            frame['relink'] = i % 37 == 19
            frame['commands'] = []
        fire = 39 if trace['profile'] == 'rifle' else 34
        for time, asset, stop in ((.6, fire, False), (4., 0, False), (4.3, 17, False), (6., 0, True)):
            trace['frames'][round(time * trace['hz'])]['commands'].append(
                dict(asset=asset, stop=stop, blend=.2, rate=1., start=0., stopGroup=True))


rewritten = base.replace(anchor, "    author_events(requests)\n" + anchor)
rewritten = rewritten.replace(montage, "montage_catalog=load('montage_catalog_v2.json') if case in ('actions','rebind','multi-layer') else None")
rewritten = rewritten.replace(source, "    source=repo/'tools/unreal/LyraLinkedMontageOracle'")
exec(compile(rewritten, str(original_base), 'exec'), globals())
assert original_base.read_bytes() == raw

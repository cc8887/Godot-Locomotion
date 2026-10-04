"""Audit actual ordinary movement, final skin observations and rendered terrain."""
from pathlib import Path
import argparse
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/lyra-analysis'
TAG = 'terrain-course-v3'
CHANGED = {'src/Als.Godot/Locomotion/LyraLocomotionDemo.cs'}
NEW = {'src/Als.Godot/Locomotion/LyraTerrainCourse.cs',
       'scripts/verify-lyra-terrain-course.ps1', 'tools/verify_lyra_terrain_course.py'}
DOCS = {'ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md'}
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())


def save(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(json.dumps(value, indent=2, allow_nan=False) + '\n')


def freeze():
    before = read(OUT / 'terrain-course-v1-before.json')
    assert {n for n, h in before.items() if sha(ROOT / n) != h} == CHANGED
    save(OUT / f'{TAG}-frozen.json', dict(
        sources={n: sha(ROOT / n) for n in sorted(CHANGED | NEW)},
        protected={n: h for n, h in before.items() if n not in CHANGED | DOCS}))
    print('TERRAIN_SOURCES_FROZEN')


def audit():
    frozen = read(OUT / f'{TAG}-frozen.json')
    for group in frozen.values():
        for n, h in group.items(): assert sha(ROOT / n) == h, n
    reports = {}; count = 0
    for config in ('debug', 'optimize'):
        summary = read(OUT / f'{TAG}-{config}-verification.json')
        assert summary['passed']
        assert {r['name'] for r in summary['runs']} == {'30', '60', '120'} | ({'render'} if config == 'debug' else set())
        if config == 'optimize': assert summary['debugRestored']
        for r in summary['runs']:
            assert r['passed'] and r['exitCode'] == 0
            assert sha(Path(r['log'])) == r['logSha256'].lower()
            assert sha(Path(r['report'])) == r['reportSha256'].lower()
            assert not re.search(r'^\s*(ERROR|WARNING):', Path(r['log']).read_text(encoding='utf-8-sig'), re.M)
            data = read(Path(r['report'])); reports[config, r['name']] = data
            assert data['actors'] == 2 and data['frames'] == data['retries'] == r['hz'] * 15
            assert data['actualJolt'] and data['ordinaryMovement'] and data['completeMain'] and data['finalRig'] and data['movementPassed']
            assert data['skinBones'] == 68 and data['logicalBones'] == 81
            assert len(data['rows']) == data['frames'] * 2
            for s in data['roles']:
                assert s['Steps'] > 0 and s['Slopes'] > 0 and s['Drops'] > 0 and s['Landings'] >= 2
                assert s['Crouch'] > 0 and s['Ads'] > 0 and .8 < s['Height'] < 1
                assert {'pistol', 'rifle'} <= set(s['profiles'])
            for row in data['rows']:
                assert row['rigQueries'] > 0
                assert all(abs(v) < 100 for v in row['position'])
            count += 1
    for hz in ('30', '60', '120'):
        assert reports['debug', hz] == reports['optimize', hz]
        assert reports['debug', hz] == read(OUT / f'terrain-course-v2-debug-{hz}.json')
    assert reports['debug', '60']['rows'] == reports['debug', 'render']['rows']
    frames = read(OUT / f'{TAG}-debug-frames/frames.json')
    names = {'overview', 'step', 'slope', 'crouch', 'drop', 'landing', 'rifle', 'pistol', 'jump'}
    assert frames['phase'] == 'FramePostDraw' and {f['name'] for f in frames['frames']} == names
    images = {n: sha(OUT / f'{TAG}-debug-frames/{n}.png') for n in names}
    for file in (OUT / f'{TAG}-optimize-debug-backup').iterdir():
        assert sha(file) == sha(ROOT / '.godot/mono/temp/bin/Debug' / file.name)
    for suffix in ('build', 'optimize-build'):
        log = (OUT / f'{TAG}-{suffix}.log').read_text(encoding='utf-8-sig')
        assert '0 个警告' in log and '0 个错误' in log
    save(OUT / f'{TAG}-audit.json', dict(passed=True, sources=len(frozen['sources']),
        protected=len(frozen['protected']), godotRuns=count, framesPerBuild=3150,
        finalSkinPublicationsPerBuild=6300, actualTerrainMovement=True, postDrawImages=images,
        visualInspectionRequired=True, hardwareKeyboardAccepted=False, goalComplete=False))
    print('TERRAIN_COURSE_AUDIT_OK')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('mode', choices=('freeze', 'audit'))
    args = parser.parse_args(); (freeze if args.mode == 'freeze' else audit)()

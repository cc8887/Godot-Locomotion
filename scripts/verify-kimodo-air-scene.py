"""Verify a freshly imported Air pack through the ordinary Demo owner."""
import argparse
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from tools.kimodo_pipeline.air import validate_pack
from tools.kimodo_pipeline.graph import read_json
from tools.kimodo_pipeline.runner import write_json, file_hash


def run(pack, output, godot, *, render=True):
    pack = Path(pack).resolve(); output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    selected = read_json(pack)
    write_json(output/'air-contract.json', validate_pack(pack, selected))
    payloads = [pack, *(ROOT/e['path'].removeprefix('res://') for e in selected['files'])]
    payloads += list((ROOT/'.godot/mono/temp/bin/Debug').glob('*.dll'))
    before = {str(p): file_hash(p) for p in payloads}
    write_json(output/'frozen-inputs.json', before)
    env = {k: v for k, v in os.environ.items() if not k.startswith('ALS_')}
    resource = 'res://'+pack.relative_to(ROOT).as_posix()
    env['ALS_RESOURCE_PACK_PATH'] = resource
    results = []
    definitions = [(f'drop-{hz}-{mode}', 'kimodo_air_diagnostic', 'KIMODO_AIR_DIAGNOSTIC_OK',
        ['--hz='+str(hz), *(['--parallel'] if mode == 'parallel' else [])], False)
        for hz in (30, 60, 120) for mode in ('single', 'parallel')]
    definitions += [(name, scene, marker, [], False) for name, scene, marker in (
        ('pose-composition', 'air_pose_smoke', 'AIR_POSE_OK'),
        ('source-owner', 'air_source_runtime_smoke', 'AIR_SOURCE_RUNTIME_OK'),
        ('falling-lean', 'falling_lean_smoke', 'FALLING_LEAN_SMOKE_OK'))]
    if render:
        definitions += [(f'rendered-{mode}', 'kimodo_air_diagnostic', 'KIMODO_AIR_DIAGNOSTIC_OK',
            ['--hz=60', *(['--parallel'] if mode == 'parallel' else [])], True) for mode in ('single', 'parallel')]
    for name, scene, marker, flags, rendered in definitions:
        dump = output/(name+'.json'); log = output/(name+'.log')
        flags = ['--disable-root-motion', *flags]
        if scene == 'kimodo_air_diagnostic': flags += ['--air-output='+str(dump)]
        if rendered: flags += ['--capture-dir='+str(output/name)]
        command = [str(godot), *([] if rendered else ['--headless']), '--path', str(ROOT),
            '--audio-driver', 'Dummy', 'res://scenes/tests/'+scene+'.tscn', '--', *flags]
        try:
            with log.open('w', encoding='utf-8') as stream:
                process = subprocess.run(command, cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=180)
            code = process.returncode
        except subprocess.TimeoutExpired:
            code = -1
        text = log.read_text(encoding='utf-8', errors='replace')
        row = dict(name=name, exitCode=code, passed=code == 0 and marker in text and 'ERROR:' not in text, log=str(log))
        if scene == 'kimodo_air_diagnostic':
            data = read_json(dump) if dump.exists() else {}
            captures = data.get('captures', [])
            capture_ok = all(c['tick'] == c['committed'] == c['presented'] and Path(c['screenshot']).is_file() for c in captures)
            row.update(report=str(dump), frames=len(data.get('frames', [])), airFrames=data.get('airFrames', 0),
                captures=len(captures), captureIdentityPassed=capture_ok, failure=data.get('failure'))
            row['passed'] &= data.get('passed') is True and data.get('resourcePack') == resource and capture_ok and (not rendered or len(captures) >= 8)
        results.append(row)
        write_json(output/'results.json', dict(status='running', pack=resource, results=results))
        print(('PASS ' if row['passed'] else 'FAIL ')+name, flush=True)
    changed = [p for p, digest in before.items() if not Path(p).is_file() or file_hash(p) != digest]
    result = dict(status='passed' if all(r['passed'] for r in results) and not changed else 'failed',
        pack=resource, results=results, inputsUnchanged=not changed, changedInputs=changed,
        coverage='real 30m falling and landing; air branch composition fixture; committed GPU snapshots',
        fullSkinAcceptance=False, manualAcceptance=False, productionReady=False)
    write_json(output/'results.json', result)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--godot', type=Path, required=True)
    parser.add_argument('--no-render', action='store_true')
    args = parser.parse_args()
    raise SystemExit(0 if run(args.pack, args.output, args.godot, render=not args.no_render)['status'] == 'passed' else 1)

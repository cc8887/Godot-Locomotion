"""Audit the character-owned Main split against frozen production/native evidence."""
import argparse
import json
import re
from pathlib import Path
from verify_lyra_multi_layer_native import ASSETS, EVIDENCE, PROJECT, ROOT, package_file, read, sha


def log_text(path):
    raw = path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig')


def clean_log(path):
    value = log_text(path)
    assert not any(line.lstrip().startswith(('ERROR:', 'WARNING:')) for line in value.splitlines()), path
    return value


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tag', required=True)
    args = parser.parse_args()
    assert re.fullmatch(r'[A-Za-z0-9_-]+', args.tag)
    tag = args.tag
    output = EVIDENCE / f'{tag}-integrity.json'
    assert not output.exists(), 'Preserve prior evidence'
    evidence = {}

    def load(path):
        evidence[str(path.relative_to(ROOT))] = sha(path)
        return read(path)

    closure = load(EVIDENCE / 'whole-main-multi-layer-v3-30-full-closure.json')
    for name, digest in closure['previousFixtureSha256'].items():
        assert sha(ASSETS / name) == digest, name
    for name, digest in closure['assetSha256'].items():
        assert sha(package_file(name)) == digest, name
    for name, digest in closure['protectedProject'].items():
        assert sha(PROJECT / name) == digest, name
    for name, digest in closure['probeSourceSha256'].items():
        assert sha(ROOT / 'tools/unreal/LyraWholeMainOracle' / name) == digest, name
        assert sha(ROOT / 'artifacts/unreal/lyra-whole-main-oracle/package-multi-layer-v1' / name) == digest, name
    for name, digest in closure['captureSourceSha256'].items():
        assert sha(ROOT / name) == digest, name
    protected = load(EVIDENCE / 'linked-layer-bindings-v1-protected.json')
    for name, digest in protected.items():
        assert sha(ROOT / name) == digest, name
    codegen = load(EVIDENCE / 'layer-codegen-v3-integrity.json')
    unchanged_contracts = [
        'src/Als.Core/Animation/AlsAnimationLayerContracts.cs',
        'src/Als.Core/Animation/AlsLinkedLayerBindings.cs',
        'src/Als.Import/Compilation/AlsAnimationLayerContractCompiler.cs',
        'src/Als.Import/Compilation/AlsAnimationLayerCodeGenerator.cs',
        'src/Als.Godot/Animation/Lyra/LyraGeneratedLayerContract.g.cs',
        'src/Als.Godot/Animation/Lyra/LyraLinkedLayerContracts.cs',
        'src/Als.Godot/Animation/Lyra/LyraMainPoseHost.cs',
        'scripts/verify-lyra-linked-layer-bindings.ps1',
    ]
    for name in unchanged_contracts:
        assert sha(ROOT / name) == codegen['sources'][name], name

    summaries = []
    processes = 0
    native = []
    for configuration, directory, build in [('debug', 'Debug', 'debug'), ('optimize', 'ExportRelease', 'export')]:
        build_path = EVIDENCE / f'{tag}-build-{build}.log'
        build_log = log_text(build_path)
        evidence[str(build_path.relative_to(ROOT))] = sha(build_path)
        assert '0 个警告' in build_log and '0 个错误' in build_log
        summary = load(EVIDENCE / f'{tag}-{configuration}-verification.json')
        baseline = load(EVIDENCE / f'layer-codegen-v3-{configuration}-verification.json')
        assert summary['passed'] and len(summary['runs']) == 11
        for name, digest in summary['assemblies'].items():
            assert sha(ROOT / '.godot/mono/temp/bin' / directory / name) == digest.lower(), name
        if configuration == 'optimize':
            assert summary['debugRestored']
            for name, digest in summaries[0]['assemblies'].items():
                assert sha(EVIDENCE / f'{tag}-optimize-debug-backup' / name) == digest.lower()
        for run, previous in zip(summary['runs'], baseline['runs'], strict=True):
            assert run['name'] == previous['name'] and run['passed'] and run['exitCode'] == 0
            path = Path(run['log'])
            assert sha(path) == run['logSha256'].lower()
            clean_log(path)
            evidence[str(path.relative_to(ROOT))] = sha(path)
            if 'report' in run:
                path = Path(run['report'])
                assert sha(path) == run['reportSha256'].lower()
                assert load(path) == load(Path(previous['report'])), run['name']
            processes += 1
        summaries.append(summary)

        for hz, reference in [(30, 'rebind30-final'), (60, 'rebind60-first'), (120, 'rebind120-final')]:
            for kind in ('request', 'native', 'closure', 'launch'):
                path = EVIDENCE / f'whole-main-{reference}-{kind}.json'
                evidence[str(path.relative_to(ROOT))] = sha(path)
            report = load(EVIDENCE / f'whole-main-{configuration}-{tag}-{hz}-verification.json')
            assert report['comparisonPassed'] and report['retry'] and report['runTag'] == reference
            assert sha(ASSETS / 'native_win64/LyraNativeMath.dll') == report['nativeMathSha256'].lower()
            assert not report['completeAcceptance']
            assert report['assemblies'] == summary['assemblies']
            assert [r['boundary'] for r in report['runs']] == ['pre-inertia', 'pre-rig', 'final']
            if configuration == 'optimize':
                assert report['debugRestored']
                for name, digest in summaries[0]['assemblies'].items():
                    assert sha(EVIDENCE / f'whole-main-optimize-{tag}-{hz}-debug-backup' / name) == digest.lower()
            for run in report['runs']:
                assert run['comparisonPassed'] and run['exitCode'] == 0
                path = Path(run['log'])
                value = clean_log(path)
                evidence[str(path.relative_to(ROOT))] = sha(path)
                frames = {30: 1080, 60: 2160, 120: 4320}[hz]
                assert f'LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames={frames} profiles=3' in value
                assert f'retry={frames} controlledPhysicalInputs=true' in value
                assert 'rebinds=24 sameClass=24 actionRebinds=21 hiddenRebinds=6 rejectedRebinds=48' in value
                match = re.search(r'mainStateFrames=(\d+) retiredCancelChecks=(\d+) foreignMainRejected=(\d+)', value)
                assert match and int(match[1]) == frames and int(match[2]) > frames and int(match[3]) == 3
                native.append(dict(configuration=configuration, hz=hz, boundary=run['boundary'],
                                   frames=frames, retiredCancelChecks=int(match[2]), foreignMainRejected=int(match[3])))
                processes += 1

    for debug, optimize in zip(summaries[0]['runs'], summaries[1]['runs'], strict=True):
        assert debug['name'] == optimize['name']
        if 'report' in debug:
            assert read(Path(debug['report'])) == read(Path(optimize['report']))

    scope = load(EVIDENCE / f'{tag}-scope-verification.json')
    path = EVIDENCE / f'{tag}-scope-run.ps1'
    evidence[str(path.relative_to(ROOT))] = sha(path)
    assert scope['passed'] and len(scope['runs']) == 2 and scope['debugRestored']
    for run, summary in zip(scope['runs'], summaries, strict=True):
        assert run['configuration'].lower() == summary['configuration'].lower()
        assert run['passed'] and run['exitCode'] == 0 and run['assemblies'] == summary['assemblies']
        path = Path(run['log'])
        assert sha(path) == run['logSha256'].lower()
        value = clean_log(path)
        assert 'LYRA_LOCOMOTION_SOURCE_SCOPE_JOINT_OK frames=3780' in value
        assert 'LYRA_MAIN_IDLE_SCOPE_FEEDBACK_OK' in value
        evidence[str(path.relative_to(ROOT))] = sha(path)
        processes += 1
    for name, digest in summaries[0]['assemblies'].items():
        assert sha(EVIDENCE / f'{tag}-scope-debug-backup' / name) == digest.lower()

    files = ['LyraMainGraphStateOwner.cs', 'LyraMainSourceScope.cs', 'LyraLocomotionSourceScope.cs',
             'LyraMainGroundResources.cs', 'LyraLocomotionResources.cs', 'LyraItemLayerGraphInstance.cs',
             'LyraMainLocomotionHost.cs', 'LyraWholeMainDiagnosticSmoke.cs', 'LyraLocomotionSourceScopeSmoke.cs']
    sources = {f'src/Als.Godot/Animation/Lyra/{name}': sha(ROOT / 'src/Als.Godot/Animation/Lyra' / name) for name in files}
    sources.update({name: sha(ROOT / name) for name in unchanged_contracts})
    for name in ['scripts/verify-lyra-whole-main-diagnostic.ps1', 'scripts/verify-lyra-linked-layer-bindings.ps1',
                 'tools/verify_lyra_main_state_owner.py']:
        sources[name] = sha(ROOT / name)
    assert processes == 42
    result = dict(auditPassed=True, characterOwnedMainStateIntegrated=True, finalGodotProcesses=processes,
                  nativeBoundaries=native, ordinaryReportsUnchanged=True, standaloneScopeFramesPerBuild=3780,
                  unchangedLyraJson=len(closure['previousFixtureSha256']),
                  unchangedOriginalPackages=len(closure['assetSha256']),
                  unchangedProjectConfiguration=len(closure['protectedProject']),
                  assemblyRestoreRounds=5, godotMultipleOwnerPoseParity=False, fullPhysicalParity=False,
                  newUeCapture=False, goalComplete=False, sources=sources, evidence=evidence)
    with output.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')
    print(output)


if __name__ == '__main__':
    main()

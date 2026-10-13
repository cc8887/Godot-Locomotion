"""Verify the actual Mantle presentation, fixed hands and full skin with unchanged gates."""
from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SCENE = 'res://scenes/tests/mantling_demo_smoke.tscn'
CASES = {
    'low': ('low', 1.), 'short-low': ('low', .75), 'medium-low': ('low', 1.2),
    'threshold-high': ('high', 1.25), 'medium-high': ('high', 1.5),
    'high': ('high', 1.8), 'nominal-high': ('high', 2.),
    'moving-low': ('moving', 1.), 'moving-high': ('moving', 1.8),
    'cancel': ('cancel', .75), 'destroy': ('destroy', .75),
    'no-hand-lock': ('low', 1.),
}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False)+'\n', encoding='utf-8')


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def resource(path):
    if not path.startswith('res://'):
        raise ValueError('Expected res:// resource: '+path)
    result = (ROOT/path[6:]).resolve()
    result.relative_to(ROOT)
    return result


def angle(a, b):
    dot = abs(sum(x*y for x,y in zip(a,b)))/math.sqrt(sum(x*x for x in a)*sum(x*x for x in b))
    return math.degrees(2*math.acos(min(1, dot)))


def summarize(data, case):
    frames = data['frames']
    if not frames:
        raise ValueError('No published frames')
    peak = max(frames, key=lambda f: f['penetration']['MaximumDepthMeters'])
    errors, angles, support = [], [], {'left': [], 'right': []}
    lengths = {(side, segment): [] for side in ('l', 'r') for segment in ('upper', 'lower')}
    ragdoll_lengths = {key: [] for key in lengths}
    for frame in frames:
        contacts = frame['handContacts']
        for side, suffix in [('left', 'l'), ('right', 'r')]:
            bones = frame['bones']
            # Mantle IK preserves arm lengths. Ragdoll publishes independently
            # simulated body transforms; its anatomical quality is a separate gate.
            observed_lengths = ragdoll_lengths if frame.get('ragdoll', False) else lengths
            observed_lengths[suffix, 'upper'].append(math.dist(bones['upperarm_'+suffix]['world'], bones['lowerarm_'+suffix]['world']))
            observed_lengths[suffix, 'lower'].append(math.dist(bones['lowerarm_'+suffix]['world'], bones['hand_'+suffix]['world']))
            if contacts[side.title()+'Weight'] < .999:
                continue
            bone, anchor = bones['hand_'+suffix], contacts[side+'Anchor']
            errors.append(math.dist(bone['world'], anchor['world']))
            angles.append(angle(bone['rotation'], anchor['rotation']))
            support[side].append(bone['platformLocal'])
    drift = max((math.sqrt(sum((max(p[i] for p in points)-min(p[i] for p in points))**2 for i in range(3)))
                 for points in support.values() if points), default=0)
    bone_error = max(max(values)-min(values) for values in lengths.values())
    ragdoll_bone_error = max((max(values)-min(values) for values in ragdoll_lengths.values() if values), default=0)
    residual = max(max(f['handContacts']['ResidualCm'], f.get('modelContactResidualCm', 0)) for f in frames)
    root_shift = max(math.sqrt(sum(f.get('modelContactRootCorrectionCm', {}).get(a, 0)**2 for a in ('X','Y','Z'))) for f in frames)
    root_limit = max(f.get('modelContactRootCorrectionLimitCm', 0) for f in frames)
    depth = peak['penetration']['MaximumDepthMeters']
    identities = all(f['committed']==f['presented'] for f in frames)
    contact = bool(errors) and max(errors)<=.001 and max(angles)<=.1 and drift<=.002 and residual<=.1
    lifecycle = case['case'] not in ('cancel', 'destroy') or (
        any(f['handContacts']['Applied'] for f in frames) and
        all(not f['handContacts']['Applied'] for f in frames if not f['active']))
    completion = next((b for a,b in zip(frames,frames[1:]) if a['active'] and not b['active']), None)
    landing_error = math.dist([completion['actor'][i] for i in (0,2)],
        [completion['destination'][i] for i in (0,2)]) if completion and completion.get('destination') else None
    # Moving-base observation allows the same one-step drift as hand contacts.
    landing_limit = .002 if case['scenario']=='moving' else .001
    landing = case['case'] in ('cancel','destroy') or landing_error is not None and landing_error<=landing_limit
    negative = case['case']=='no-hand-lock'
    if negative:
        # The unchanged skin gate must actually reject the disabled controller.
        contact = not any(f['handContacts']['Applied'] for f in frames)
    passed = contact and lifecycle and landing and identities and bone_error<=.00001 and root_shift<=root_limit+.00001
    passed = passed and (depth>.02 if negative else depth<=.02)
    return dict(passed=passed, expectedSkinRejection=negative, skinPassed=depth<=.02,
        contactPassed=contact, lifecyclePassed=lifecycle, landingPassed=landing,
        landingHorizontalErrorMeters=landing_error, identitiesPassed=identities,
        frames=len(frames), vertices=peak['penetration']['Vertices'], peakTick=peak['tick'], peakBone=peak['penetration']['DominantBone'],
        maximumSkinDepthMeters=depth, maximumAnchorErrorMeters=max(errors, default=0),
        maximumPalmErrorDegrees=max(angles, default=0), maximumPlatformDriftMeters=drift,
        maximumReachResidualCm=residual, supportSamples=len(errors), maximumArmLengthVariationMeters=bone_error,
        ragdollFrames=sum(bool(f.get('ragdoll', False)) for f in frames),
        maximumRagdollArmLengthVariationMeters=ragdoll_bone_error,
        maximumModelRootCorrectionCm=root_shift, modelRootCorrectionLimitCm=root_limit,
        displayedSkeleton=peak.get('presentationSkeleton'), actorEnd=frames[-1]['actor'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--cases', nargs='+', choices=CASES, default=['low', 'high'])
    parser.add_argument('--models', nargs='+', choices=('mannequin', 'girl'), default=['mannequin', 'girl'])
    parser.add_argument('--roots', nargs='+', choices=('off', 'on'), default=['off', 'on'])
    parser.add_argument('--hz', nargs='+', type=int, choices=(30,60,120), default=[60])
    parser.add_argument('--modes', nargs='+', choices=('single', 'parallel'), default=['parallel'])
    parser.add_argument('--overlay', choices=('Default','Injured'), default='Default')
    parser.add_argument('--render', action='store_true')
    parser.add_argument('--godot', type=Path, default=ROOT/'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe')
    args = parser.parse_args()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    pack_path = resource(args.pack); pack = read(pack_path)
    controls = [pack_path, ROOT/'assets/config/als_resource_pack.json', ROOT/'assets/config/als_character_model.json',
        ROOT/'assets/config/als_animation_settings.json', Path(__file__).resolve()]
    for item in pack['files']:
        path = resource(item['path']); path.relative_to(pack_path.parent)
        if sha(path)!=item['sha256']:
            raise ValueError('Changed candidate payload: '+str(path))
        controls.append(path)
    controls += list((ROOT/'.godot/mono/temp/bin/Debug').glob('*.dll'))
    controls += [ROOT/p for p in (
        'src/Als.Core/Actions/AlsMantlingRootMotion.cs', 'src/Als.Core/Actions/AlsMantlingFrame.cs',
        'src/Als.Core/Locomotion/AlsSkeletonPoseRetargetModel.cs',
        'src/Als.Core/Locomotion/AlsRetargetContactState.cs', 'src/Als.Core/Contracts/AlsRuntimeState.cs',
        'src/Als.Core/Locomotion/AlsRetargetBodyGeometry.cs',
        'src/Als.Core/Locomotion/AlsRetargetContactPreparation.cs', 'src/Als.Core/Actions/AlsMantlingContactCurve.cs',
        'src/Als.Godot/Animation/AlsBaseLayerMantling.cs',
        'src/Als.Godot/Animation/AlsBaseLayerFrameRuntime.cs',
        'src/Als.Import/Compilation/AlsRefactoredV4ViewMask.cs',
        'src/Als.Core/Locomotion/AlsTwistBoneReconstruction.cs', 'src/Als.Core/Locomotion/AlsMantlingHandSolver.cs',
        'src/Als.Godot/Animation/AlsAnimationRuntimeOptions.cs', 'src/Als.Godot/Animation/AlsProductionMovementRuntime.cs',
        'src/Als.Godot/Animation/AlsLocomotionAnimationController.cs', 'src/Als.Godot/Locomotion/AlsCharacterMantling.cs',
        'src/Als.Godot/Locomotion/AlsCharacterMotor.cs',
        'src/Als.Godot/Locomotion/AlsCharacterModelBinding.cs', 'src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs',
        'src/Als.Godot/Locomotion/AlsSoleGeometryObserver.cs',
        'src/Als.Godot/Locomotion/AlsP3Character.cs', 'src/Als.Godot/Locomotion/MantlingDemoSmoke.cs',
        'scenes/tests/mantling_demo_smoke.tscn', 'assets/models/als_girl/girl_high_als_retarget.tscn')]
    frozen = {str(p):sha(p) for p in controls}
    cases = [dict(name=f'{name}-{model}-{root}-{hz}-{mode}',case=name,model=model,root=root,hz=hz,mode=mode,
                  scenario=CASES[name][0],height=CASES[name][1])
             for name in args.cases for model in args.models for root in args.roots for hz in args.hz for mode in args.modes]
    write(output/'manifest.json', dict(pack=args.pack,packSha256=sha(pack_path),cases=cases,frozenInputs=frozen,
        rendered=args.render,overlay=args.overlay,productionReady=False))

    def run(case):
        name=case['name']; log=output/(name+'.log'); trace=output/(name+'.json')
        flags=['--mantle-single'] if case['mode']=='single' else []
        if case['case']=='no-hand-lock': flags.append('--mantle-no-hand-lock')
        display=['--rendering-method','gl_compatibility','--resolution','1280x720','--max-fps','60','--audio-driver','Dummy'] if args.render else ['--headless','--fixed-fps','60']
        if args.render: flags.append('--capture-dir='+str(output/name))
        command=[str(args.godot.resolve()),'--path',str(ROOT),*display,SCENE,'--',
            '--als-model='+case['model'],'--'+('enable' if case['root']=='on' else 'disable')+'-root-motion',
            '--mantle-case='+case['scenario'],'--mantle-height='+str(case['height']),'--mantle-hz='+str(case['hz']),
            '--overlay='+args.overlay,'--mantle-output='+str(trace),*flags]
        env=os.environ.copy()
        for key in ('ALS_ASSET_ROOT','ALS_RAW_SOURCE_GENERATED_DIR','ALS_REQUIRE_KIMODO_SOURCE','ALS_RAW_SOURCE_REQUEST_OUTPUT'):
            env.pop(key,None)
        env['ALS_RESOURCE_PACK_PATH']=args.pack
        result=dict(**case,command=command,runtimePassed=False,passed=False)
        try:
            with log.open('w',encoding='utf-8') as stream:
                process=subprocess.run(command,env=env,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,timeout=180,
                    creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
            text=log.read_text(encoding='utf-8')
            result.update(exitCode=process.returncode,runtimePassed=process.returncode==0 and 'MANTLE_DEMO_OK' in text and 'ERROR:' not in text)
            if trace.exists(): result.update(summarize(read(trace),case))
            result['passed']=result['passed'] and result['runtimePassed']
        except Exception as error:
            result.update(passed=False,error=str(error))
        print(name,'passed='+str(result['passed']),'skin_cm='+str(result.get('maximumSkinDepthMeters',0)*100),flush=True)
        return result

    with ThreadPoolExecutor(max_workers=2) as pool:
        results=list(pool.map(run,cases))
    changed=[p for p,digest in frozen.items() if sha(Path(p))!=digest]
    report=dict(schemaVersion=1,pack=args.pack,packSha256=sha(pack_path),checks=results,
        changedFiles=changed,inputsUnchanged=not changed,productionReady=False,
        scope='Actual displayed hands, immutable arm lengths, full skin and existing character publication',
        passed=all(r['passed'] for r in results) and not changed)
    write(output/'results.json',report)
    return 0 if report['passed'] else 1


if __name__=='__main__':
    raise SystemExit(main())

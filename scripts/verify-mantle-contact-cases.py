"""Reuse the real mantle scene for height, moving-base and cancellation contacts.

Reports contact/runtime and skin acceptance separately; any skin failure keeps
the overall result failed. Uses no desktop automation or Unreal.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import math
import os
from pathlib import Path
import subprocess
from verify_kimodo_mantle_scene_support import ROOT, read, sha, write


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack',required=True);parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--include-original',action='store_true',help='Additional original High asset controls')
    parser.add_argument('--render',action='store_true',help='GPU captures of selected medium/high cases; explicit Dummy audio')
    parser.add_argument('--only',action='append',help='Repeat selected named cases without changing acceptance thresholds')
    parser.add_argument('--hz',type=int,choices=(30,60,120),default=60)
    parser.add_argument('--overlay',choices=('Default','Injured'),default='Default',help='Verify both Low support-hand selections')
    parser.add_argument('--godot',type=Path,default=ROOT/'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe')
    args=parser.parse_args();output=args.output.resolve();output.mkdir(parents=True,exist_ok=False)
    pack_path=ROOT/args.pack.removeprefix('res://');pack=read(pack_path)
    controls=[pack_path,*[ROOT/e['path'].removeprefix('res://') for e in pack['files']],
        *list((ROOT/'assets/generated/mantle_contacts').glob('*.json')),
        *list((ROOT/'.godot/mono/temp/bin/Debug').glob('*.dll')),
        *list((ROOT/'assets/config').glob('refactored_mantle*.json')),Path(__file__).resolve(),
        ROOT/'scripts/verify_kimodo_mantle_scene_support.py']
    frozen={str(p):sha(p) for p in controls}
    cases=[('short-low','low',.75,False,[]),('moving-low','moving',1.,False,[]),
           ('medium-low','low',1.2,False,[]),('threshold-high','high',1.25,False,[]),
           ('medium-high','high',1.5,False,[]),
           ('high','high',1.8,False,[]),('nominal-high','high',2.,False,[]),
           ('moving-high','moving',1.8,False,[]),
           ('cancel','cancel',.75,False,[]),('destroy','destroy',.75,False,[]),
           ('no-hand-lock','low',1.,False,['--mantle-no-hand-lock'])]
    if args.include_original:
        cases += [('original-high','high',1.8,True,[])]
    if args.only:
        unknown=set(args.only)-{c[0] for c in cases}
        if unknown:raise ValueError('Unknown mantle cases: '+str(sorted(unknown)))
        cases=[c for c in cases if c[0] in args.only]
    def run(case):
        name,scenario,height,original,flags=case;log=output/(name+'.log');frames=output/(name+'.json')
        rendered=args.render and name in ('medium-low','medium-high','high','nominal-high')
        display=['--rendering-method','gl_compatibility','--resolution','1280x720','--max-fps','60','--audio-driver','Dummy'] if rendered else ['--headless','--fixed-fps','60']
        captures=['--capture-dir='+str(output/name)] if rendered else []
        command=[str(args.godot.resolve()),
            '--path',str(ROOT),*display,'res://scenes/tests/mantling_demo_smoke.tscn','--',
            '--disable-root-motion','--mantle-case='+scenario,'--mantle-height='+str(height),'--mantle-hz='+str(args.hz),
            '--overlay='+args.overlay,
            '--mantle-output='+str(frames),*captures,*flags]
        env=dict(os.environ,ALS_RESOURCE_PACK_PATH='original' if original else args.pack)
        with log.open('w',encoding='utf-8') as stream:
            process=subprocess.run(command,env=env,cwd=ROOT,stdout=stream,stderr=subprocess.STDOUT,timeout=180)
        text=log.read_text(encoding='utf-8');runtime=process.returncode==0 and 'MANTLE_DEMO_OK' in text and 'ERROR:' not in text
        result=dict(name=name,heightMeters=height,originalControl=original,rendered=rendered,
            runtimePassed=runtime,contactPassed=False,errors=[])
        if not runtime:result['errors'].append('Real scene assertions did not complete')
        if frames.exists():
            observations=read(frames)['frames']
            if not observations:
                result['errors'].append('No published frames; initialization failed')
                return result
            error=angle=residual=0.;support={'left':[],'right':[]}
            for frame in observations:
                contacts=frame['handContacts']
                for side,suffix in [('left','l'),('right','r')]:
                    if contacts[side.title()+'Weight']<.999:continue
                    bone=frame['bones']['hand_'+suffix];anchor=contacts[side+'Anchor'];support[side].append(bone)
                    error=max(error,math.dist(bone['world'],anchor['world']));residual=max(residual,contacts['ResidualCm'])
                    q=bone['rotation'];r=anchor['rotation'];dot=abs(sum(a*b for a,b in zip(q,r)))/math.sqrt(sum(a*a for a in q)*sum(b*b for b in r))
                    angle=max(angle,math.degrees(2*math.acos(min(1,dot))))
            drift={side:math.sqrt(sum((max(p['platformLocal'][i] for p in points)-min(p['platformLocal'][i] for p in points))**2 for i in range(3)))
                   for side,points in support.items() if points}
            contact=bool(drift) and error<=.001 and angle<=.1 and residual<=.1 and max(drift.values())<=.002
            if name in ('cancel','destroy'):
                contact=any(f['handContacts']['Applied'] for f in observations) and all(not f['handContacts']['Applied'] for f in observations if not f['active'])
            if name=='no-hand-lock':contact=not any(f['handContacts']['Applied'] for f in observations)
            depth=max(f['penetration']['MaximumDepthMeters'] for f in observations)
            result.update(contactPassed=contact,maximumAnchorErrorMeters=error,maximumPalmErrorDegrees=angle,
                maximumReachResidualCm=residual,platformLocalDriftMeters=drift,maximumSkinDepthMeters=depth,
                maximumRequestedCorrectionCm=max(f['handContacts'].get('RequestedCorrectionCm',0) for f in observations),
                maximumReachExcessCm=max(f['handContacts'].get('ReachExcessCm',0) for f in observations),
                skinPassed=depth<=.02,frames=len(observations),identitiesPassed=all(f['committed']==f['presented'] for f in observations))
            if not contact:result['errors'].append('Hand contact acceptance failed')
            if depth>.02:result['errors'].append('Published skin exceeds unchanged 2 cm threshold')
        print(name,'runtime',runtime,'contact',result['contactPassed'],flush=True);return result
    with ThreadPoolExecutor(max_workers=2) as pool:results=list(pool.map(run,cases))
    changed=[p for p,digest in frozen.items() if sha(Path(p))!=digest]
    report=dict(schemaVersion=1,scope='Height, platform-local contacts and cancellation; full skin gate retained',
        computerUse=False,productionReady=False,hz=args.hz,overlay=args.overlay,requestedCases=args.only,checks=results,changedFiles=changed,inputsUnchanged=not changed,
        passed=all(r['runtimePassed'] and r['contactPassed'] and r.get('skinPassed') and r.get('identitiesPassed') for r in results) and not changed)
    write(output/'results.json',report);return 0 if report['passed'] else 1


if __name__=='__main__':raise SystemExit(main())

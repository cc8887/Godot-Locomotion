"""Diagnostic original full-root execution; does not publish an acceptance fixture."""
import copy
import hashlib
import json
import os
from pathlib import Path
import unreal

repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
out=repo/'artifacts/lyra-analysis'
tag=os.environ['LYRA_WHOLE_RUN_TAG']
assert tag.replace('-','').replace('_','').isalnum()
hz=int(os.environ.get('LYRA_WHOLE_HZ','60'));limit=int(os.environ.get('LYRA_WHOLE_FRAME_LIMIT','60'))
case=os.environ.get('LYRA_WHOLE_CASE','movement')
assert hz in (30,60,120) and limit>=0 and case in ('movement','turn','actions','rebind','physics','multi-layer')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
load=lambda p:json.loads((root/p).read_bytes())
previous={p.relative_to(root).as_posix():sha(p) for p in root.rglob('*.json')}
project=Path(unreal.Paths.get_project_file_path())
protected={p.relative_to(project.parent).as_posix():sha(p) for p in [project,*(project.parent/'Config').rglob('*.ini')]}
packages=load('named_notify_v1_policy.json')['assetSha256']
contracts=load('linked_layer_contracts.json')
for alias,digest in contracts['assetSha256'].items():
    path=contracts['classes'][alias]['class']
    assert packages.get(path,digest)==digest
    packages[path]=digest
def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return project.parent/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)
movement_sources={}
if case=='physics':
    character='/ShooterCore/Game/B_Hero_ShooterMannequin.B_Hero_ShooterMannequin_C'
    packages[character]=sha(package_file(character))
    movement_sources={str(p.relative_to(project.parent)).replace('\\','/'):sha(p) for p in
        (project.parent/'Source/LyraGame/Character').glob('LyraCharacter*.cpp')}
def protect():
    for p,d in previous.items():assert sha(root/p)==d,p
    for p,d in protected.items():assert sha(project.parent/p)==d,p
    for p,d in packages.items():assert sha(package_file(p))==d,p
    for p,d in movement_sources.items():assert sha(project.parent/p)==d,p
def save(kind,value):
    p=out/f'whole-main-{tag}-{kind}.json'
    with p.open('x',encoding='utf-8',newline='\n') as f:f.write(json.dumps(value,separators=(',',':'),allow_nan=False)+'\n')
    return p

assert not (out/f'whole-main-{tag}-request.json').exists()
protect()
try:
    assert unreal.AlsLyraControlRigLibrary.prefer_raw_track_data_model(True)
    cal=load('logical_controls/calibration.json')['calibration']
    quat=unreal.Quat(*cal['handBasis']['rotation'])
    mesh=unreal.load_asset(cal['sourceMesh'])
    skeleton=unreal.AlsLyraControlRigLibrary.create_weapon_skeleton(mesh.get_editor_property('skeleton'),unreal.load_asset(cal['targetMesh']).get_editor_property('skeleton'),quat)
    assert skeleton
    catalog=load('logical_controls/catalog.json')['entries']
    extras=load('locomotion_extras/catalog.json')['entries']
    lean=load('main_lean/catalog.json')['entries']
    entries={e['target']:e for e in [*catalog,*extras,*lean]}
    sequences={}
    def create(e,base=None):
        p=e['target']
        if p in sequences:return sequences[p]
        source=unreal.load_asset(e['source'])
        s=unreal.AlsLyraControlRigLibrary.create_weapon_sequence(source,unreal.load_asset(p),skeleton,quat,base)
        assert s,p
        # Original source notifies were deliberately absent from older pose-only
        # references. Restore them on transient sequences for full graph feedback.
        assert unreal.LyraProxyUpdateOracleLibrary.copy_source_notifies(source,s),p
        unreal.AlsSourceAnimationLibrary.finish_source_compression(s)
        sequences[p]=s
        return s
    for e in entries.values():
        if not e['additive']:create(e)
    graph=load('main_layer_graph_v1.json')
    spaces=[]
    for profile in ('unarmed','pistol','rifle'):
        rows=sorted([e for e in catalog if e['slot'].startswith('aim_'+profile+'_')],key=lambda e:e['sampleIndex'])
        base=create(next(e for e in rows if e['point'][:2]==[0,0]))
        for e in rows:create(e,base)
        spaces.append(dict(source=graph['classes'][profile]['defaults']['fields']['IdleAimOffset']['value'],samples=[e['target'] for e in rows]))
    base=create(lean[0])
    for e in lean:create(e,base)
    spaces.append(dict(source=load('main_lean/inventory.json')['source'],samples=[e['target'] for e in lean]))
    for e in entries.values():
        if e['target'] not in sequences:create(e)
    montage_catalog=load('montage_catalog_v2.json') if case in ('actions','rebind') else None
    montage_targets={}
    if montage_catalog:
        inventory=load('montage_actions/inventory.json')
        for source,row in inventory['externalBases'].items():
            montage_targets[source]=row['target']
            create(dict(source=source,target=row['target'],additive=False))
        for e in inventory['rows']:
            if not e['additive']:
                create(e)
                montage_targets[e['source']]=e['target']
        for e in inventory['rows']:
            if e['additive']:
                source_base=montage_catalog['sequences'][e['source']]['baseAsset']
                base=sequences[montage_targets[source_base]] if source_base and source_base!=e['source'] else None
                create(e,base)
                montage_targets[e['source']]=e['target']
    resources=load('locomotion_resources.json')
    seeds=load('main_als_locomotion_v1_requests.json')['traces']
    requests=dict(mainClass=graph['classes']['main']['classPath'],sequencePaths=list(sequences),
        sourceTargets={e['source']:e['target'] for e in entries.values()},spaces=spaces,traces=[])
    if montage_catalog:
        requests['montagePaths']=[e['path'] for e in montage_catalog['assets']]
        requests['montageTargets']=montage_targets
    for profile in ('unarmed','pistol','rifle'):
        seed=next(t for t in seeds if t['profile']==profile and t['hz']==hz and t['case']==('movement' if case in ('actions','rebind','physics','multi-layer') else case))
        frames=[]
        for old in (seed['frames'][:limit] if limit else seed['frames']):
            o=copy.deepcopy(old['observation']);o.pop('snapshot',None)
            frames.append(dict(delta=old['delta'],observation=o,evaluate=True,
                mainProperties={'GameplayTag_IsADS':o['ads'],'GameplayTag_IsFiring':o['firing'],'GameplayTag_IsDashing':o['dashing'],
                    'bEnableRootYawOffset':o['enabled'],'GroundDistance':old['groundDistance']},
                layerProperties={}))
        if case=='rebind':
            current=profile
            profiles=('unarmed','pistol','rifle')
            # Commands run after Evaluate/Notify dispatch. Rebind follows any
            # Montage command on that frame, matching the Godot idle boundary.
            rebind_schedule=[(.75,False),(.8,True),(1.1,False),(1.2,True),
                (2.05,True),(2.15,False),(4.1,True),(4.2,False),
                (5.3,True),(5.4,False),(6.1,True),(6.2,False),
                (8.1,True),(8.2,False),(10.1,True),(10.2,False)]
            for time,changed in rebind_schedule:
                if changed:current=profiles[(profiles.index(current)+1)%len(profiles)]
                index=round(time*hz)
                if index<len(frames):
                    assert 'rebind' not in frames[index], 'Rebind schedule collision'
                    binding_seed=next(t for t in seeds if t['profile']==current and t['hz']==hz and t['case']=='movement')
                    frames[index]['rebind']=dict(profile=current,**{'class':resources['providers'][current]['class']},
                        changed=changed,bindings=binding_seed['bindings'])
        if case in ('actions','rebind'):
            equip,fire,reload,melee=(33,34,35,27) if profile=='pistol' else (38,39,40,30) if profile=='rifle' else (13,34,35,27)
            schedule=[(.5,equip,False),(1.,fire,False),(1.4,14,False),(2.,reload,False),
                (2.3,fire,False),(3.,melee,False),(3.15,17,False),(4.,3,False),
                (4.3,25,False),(5.,0,False),(6.,0,True),(7.,fire,False),(8.,reload,False),(9.,reload,True)]
            for f in frames:f['commands']=[]
            for time,asset,stop in schedule:
                index=round(time*hz)
                if index<len(frames):frames[index]['commands'].append(dict(asset=asset,stop=stop,blend=.2,rate=1.,start=0.,stopGroup=True))
        if case=='physics':
            frames=frames[:limit or hz*8]
            for i,f in enumerate(frames):
                t=i/hz
                direction=[1,0,0] if .5<=t<1.6 or 3.6<=t<5.6 else [-1,0,0] if 1.6<=t<2.1 else [0,1,0] if 2.6<=t<3.6 or 6<=t<7 else [0,0,0]
                f['control']=dict(direction=direction,yaw=45 if 2.6<=t<3.6 else 0,pitch=15 if 2.6<=t<3.6 else 0,crouching=6<=t<7,jump=i==round(4*hz))
                f['mainProperties']['GameplayTag_IsADS']=2.6<=t<3.6
                f['mainProperties']['GameplayTag_IsFiring']=False
                f['mainProperties']['GameplayTag_IsDashing']=False
        trace=dict(profile=profile,hz=hz,case=case,**{'class':resources['providers'][profile]['class']},bindings=seed['bindings'],alsReference=True,frames=frames)
        if case=='physics':
            trace['characterClass']='/ShooterCore/Game/B_Hero_ShooterMannequin.B_Hero_ShooterMannequin_C'
            trace['obstacles']=[dict(center=[650,0,100],extent=[25,5000,100])]
        # The probe's controlled execution schedule starts at frame 1 for every
        # fresh trace. This authored clock never uses a native animation result.
        for index,frame in enumerate(frames):frame['proxyExternalFrame']=index+1
        requests['traces'].append(trace)
    if case=='multi-layer':
        declarations=load('linked_layer_contracts.json')['classes']['interface']['functions']
        layouts={
            'single':{f['name']:'ItemAnimLayers' for f in declarations},
            'three-groups':{f['name']:('Controls' if f['name'] in ('FullBody_SkeletalControls','LeftHandPose_OverrideState') else 'Aim' if f['name'] in ('FullBody_Aiming','FullBodyAdditives') else 'Body') for f in declarations},
            'mixed':{f['name']:('Controls' if f['name'] in ('FullBody_SkeletalControls','LeftHandPose_OverrideState') else '' if f['name'] in ('FullBody_Aiming','FullBodyAdditives') else 'Body') for f in declarations},
            'per-call':{f['name']:'' for f in declarations}}
        expanded=[]
        for trace in requests['traces']:
            for layout,groups in layouts.items():
                variant=copy.deepcopy(trace);variant['layout']=layout;variant['functionGroups']=groups
                for index,frame in enumerate(variant['frames']):
                    if index%37==19:frame['relink']=True
                expanded.append(variant)
        requests['traces']=expanded
    save('request',requests)
    if case=='physics':
        fields=('CrouchStateChange','ADSStateChanged','IsCrouching','GameplayTag_IsADS','HasVelocity','HasAcceleration')
        rule_traces=[]
        for trace in requests['traces']:
            rows=[]
            for bits in range(64):
                main={k:bool(bits&(1<<i)) for i,k in enumerate(fields)}
                main.update(RootYawOffset=0,GameplayTag_IsFiring=False,IsJumping=False)
                rows.append(dict(main=main,montage=False))
            rule_traces.append(dict(profile=trace['profile'],**{'class':trace['class']},rows=rows))
        rule_request=dict(traces=rule_traces)
        rule_result=json.loads(unreal.AlsLyraIdleRuleLibrary.read_idle_rules(
            unreal.load_class(None,requests['mainClass']),mesh,json.dumps(rule_request)))
        save('stance-rules',dict(request=rule_request,native=rule_result))
        assert len(rule_result['traces'])==3
        for authored,actual in zip(rule_traces,rule_result['traces'],strict=True):
            assert authored['profile']==actual['profile'] and len(actual['rows'])==64
            for a,n in zip(authored['rows'],actual['rows'],strict=True):
                stance=[r for r in n['rules'] if r['machine']==2]
                assert len(stance)==2 and all(r['result']==a['main']['CrouchStateChange'] for r in stance)
    unreal.log('LYRA_WHOLE_MAIN_RECONSTRUCTED sequences='+str(len(sequences))+' traces='+str(len(requests['traces'])))
    # Providers have independent worlds/instances. Serialize each complete
    # trajectory separately to avoid UE's 32-bit FString archive size limit;
    # do not split/reset any trajectory's animation history.
    native=dict(traces=[])
    if case=='multi-layer':native['traceFiles']=[]
    for index,trace in enumerate(requests['traces']):
        batch=dict(requests,traces=[trace])
        text=unreal.LyraProxyUpdateOracleLibrary.read_trace(mesh,skeleton,list(sequences.values()),json.dumps(batch,separators=(',',':')))
        assert text,'Empty whole Main result'
        result=json.loads(text);assert len(result['traces'])==1
        if case=='multi-layer':
            captured=result['traces'][0];captured['layout']=trace['layout']
            path=save('native-'+str(index).zfill(2),captured)
            native['traceFiles'].append(dict(file=path.name,sha256=sha(path)))
            native['traces'].append({k:v for k,v in captured.items() if k!='frames'})
            del captured,result,text
        else:native['traces'].extend(result['traces'])
        unreal.log('LYRA_WHOLE_MAIN_PROVIDER_CAPTURED profile='+trace['profile']+' frames='+str(len(trace['frames'])))
    def captured_traces():
        if 'traceFiles' in native:
            for entry in native['traceFiles']:
                path=out/entry['file'];assert sha(path)==entry['sha256']
                yield json.loads(path.read_bytes())
        else:yield from native['traces']
    assert len(native['traces'])==len(requests['traces'])
    if case=='multi-layer':
        for actual,authored in zip(captured_traces(),requests['traces'],strict=True):
            actual['layout']=authored['layout']
            assert actual['multipleOriginalGraphInstances']
            assert actual['instanceCount']=={'single':1,'three-groups':3,'mixed':4,'per-call':14}[authored['layout']]
            for frame,request_frame in zip(actual['frames'],authored['frames'],strict=True):
                assert len(frame['calls'])==14 and all(c['owner']>=0 for c in frame['calls'])
                for c in frame['calls']:
                    group=authored['functionGroups'][c['function']]
                    assert c['group']==(group or 'None')
                assert len(frame['instancesAfter'])==actual['instanceCount']
                if request_frame.get('relink'):assert frame['instancesAfter']==frame['instancesRelinked']

    masks=load('main_composition_v2_policy.json')['policies']
    for trace,request in zip(captured_traces(),requests['traces'],strict=True):
        assert len(trace['frames'])==len(request['frames'])
        current=request['profile']
        for frame,authored in zip(trace['frames'],request['frames']):
            assert frame['updates'],'Whole Main did not visit a linked layer'
            assert frame['layerOutputs'],'Whole Main did not evaluate a linked layer'
            assert len(frame['output']['pose'])==81
            assert frame['upperWeights']==masks[current]['mask'],'Wrong target skeleton mask cache'
            if case=='rebind':
                assert frame['binding']['profile']==current
                if 'rebind' in authored:
                    assert frame['rebind']['changed']==authored['rebind']['changed']
                    assert frame['rebind']['after']['profile']==authored['rebind']['profile']
                    current=authored['rebind']['profile']
        # Idle legitimately has no curves; moving source output must still reach
        # the full root. Do not require manufactured zero curves on idle frames.
        assert any(frame['output']['curves'] for frame in trace['frames'])
        if case=='physics':
            assert trace['actualCharacterMovement'] and trace['motorProfile']['movementClass']=='/Script/LyraGame.LyraCharacterMovementComponent'
            assert len(trace['velocityKernel'])==250
            physical=[r['physicalInput'] for r in trace['frames']]
            contacts=sum(abs(r['location'][0]-590)<.1 and abs(r['velocity'][0])<.01 and a['control']['direction'][0]>0
                for r,a in zip(physical,request['frames'],strict=True))
            coverage=dict(airFrames=sum(r['movementMode']==3 for r in physical),crouchFrames=sum(r['crouching'] for r in physical),
                barrierContacts=contacts,maximumX=max(r['location'][0] for r in physical),floorStopFrames=sum(r['ground'] and r['velocity']==[0,0,0] for r in physical),
                rigHitFrames=sum(any(q['hit'] for q in r['rigCollision']['queries']) for r in trace['frames']))
            trace['physicsCoverage']=coverage
            if len(physical)>=hz*8:
                assert coverage['airFrames']>0 and coverage['crouchFrames']>0 and contacts>=hz//2 and coverage['maximumX']<=590.1 and coverage['rigHitFrames']>0
    save('native',native)
    if case=='physics':save('physics-coverage',dict(traces=[dict(profile=t['profile'],**t['physicsCoverage']) for t in native['traces']]))
    source=repo/'tools/unreal/LyraProxyUpdateOracle'
    save('closure',dict(previousFixtureSha256=previous,protectedProject=protected,assetSha256=packages,movementSourceSha256=movement_sources,
        captureSourceSha256={str(p.relative_to(repo)).replace('\\','/'):sha(p) for p in (Path(__file__),repo/'scripts/capture-lyra-proxy-update.ps1')},
        probeSourceSha256={p.relative_to(source).as_posix():sha(p) for p in source.rglob('*') if p.is_file()},
        scope=dict(originalFullRoot=True,originalUnifiedSync=True,originalUpdateFunction=True,originalProxyPhaseReads=True,
            controlledExternalFrames=True,mainUpdateCounterWrites=0,requiredBonesCounterAdaptation=True,
            controlledPhysicalInputs=case!='physics',authoredControls=case=='physics',
            actualCharacterMovementSimulation=case=='physics',multipleOriginalLayerGraphs=case=='multi-layer',als81=True,originalTargetMasks=True,physicalSnapshots=True,case=case,finalControlRig=True,comparisonPassed=False,goalComplete=False)))
    unreal.log('LYRA_WHOLE_MAIN_CAPTURE_OK traces='+str(len(native['traces']))+' frames='+str(sum(len(t['frames']) for t in requests['traces']))+' assets_saved=0')
finally:
    protect()

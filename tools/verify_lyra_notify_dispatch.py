"""Verify original notify query, final physical matrices and preserved assets."""
import hashlib
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/lyra-analysis'
ASSETS=ROOT/'assets/generated/lyra_als'
PROJECT=ROOT.parent/'GASP58'
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
_hashes={}
def sha(p):
    if p not in _hashes:_hashes[p]=hashlib.sha256(p.read_bytes()).hexdigest()
    return _hashes[p]
def require(v,m):
    if not v:raise RuntimeError(m)
def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return PROJECT/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return PROJECT/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)
def main():
    result_path=OUT/'notify-dispatch-verification.json';require(not result_path.exists(),'Preserve final verification')
    for name in ('notify-dispatch-native-property','notify-dispatch-native-independent'):
        log=(OUT/(name+'.log')).read_text(encoding='utf-8-sig')
        require('LYRA_NOTIFY_DISPATCH_NATIVE_OK traces=237 frames=36920 machine=7' in log and 'NOTIFY_DISPATCH_PROCESS_EXIT=0' in log,'Original UE did not complete')
        require(not re.search(r'Error:|Ensure condition failed|Assertion failed|Fatal error|Traceback \(most recent',log),'Original reference failed')
    policy=read(ASSETS/'notify_dispatch_v1_policy.json');native=read(ASSETS/'notify_dispatch_v1_native.json')
    require(native['policySha256']==sha(ASSETS/'notify_dispatch_v1_policy.json') and native['requestSha256']==sha(ASSETS/'notify_dispatch_v1_requests.json'),'Stale reference closure')
    require(policy['trace']['mainMachineContextIndex']==7 and policy['trace']['mainMachineNotifyMetadata'],'Incorrect source-state context')
    for p,d in policy['probeSourceSha256'].items():
        require(sha(ROOT/'tools/unreal/LyraNotifyDispatchOracle'/p)==d,'Probe source changed')
        require(sha(OUT.parent/'unreal/lyra-notify-dispatch-oracle/package-property'/p)==d,'Compiled probe differs')
    require(not (PROJECT/'Plugins/LyraNotifyDispatchOracle').exists(),'Optional probe left installed')
    rows=[]
    for configuration in ('debug','optimize'):
        log=(OUT/f'notify-dispatch-{configuration}-final2.log').read_text(encoding='utf-8-sig')
        exits=re.findall(r'LYRA_NOTIFY_DISPATCH_RUN_EXIT name=(\S+) code=(\d+)',log)
        require(len(exits)==11 and all(code=='0' for _,code in exits),'Eleven-process matrix incomplete')
        require(not re.search(r'^\s*(?:ERROR|WARNING):',log,re.M),'Godot matrix issue')
        require('LYRA_NOTIFY_DISPATCH_MATRIX_OK' in log and 'frames=36920 retries=36920 pivotTrue=8674 named=22 reachedEndHistory=201 inactiveHistory=1' in log,'Original rule/history comparison incomplete')
        require('LYRA_EMOTE_PHYSICS_GODOT_OK' in log and 'LYRA_MOTION_WARPING_PHYSICS_GODOT_OK' in log and 'LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK' in log,'Prior physical gates missing')
        loaded=dict(re.findall(r'LYRA_NOTIFY_DISPATCH_ASSEMBLY file=(\S+) sha256=(\w+)',log))
        directory=ROOT/'.godot/mono/temp/bin'/('Debug' if configuration=='debug' else 'ExportRelease')
        require(len(loaded)==3,'Loaded assemblies missing')
        for p,d in loaded.items():require(sha(directory/p).upper()==d,'Matrix assembly changed')
        for hz in (30,60,120):
            row=read(OUT/f'notify-dispatch-{configuration}-{hz}-final2.json')
            require(row['roles']==6 and row['frames']==hz*8 and row['moves']==row['retries']==hz*48 and row['switches']==12,'Actual Pivot matrix incomplete')
            require(row['pivotFrames']>0 and row['notifyFrames']>0 and row['notifyExits']>0 and row['actualAlsModel'] and row['actualJolt'],'Physical Pivot path absent')
            if configuration=='optimize':
                require(row==read(OUT/f'notify-dispatch-debug-{hz}-final2.json'),'Optimized physical history differs')
                require(read(OUT/f'notify-dispatch-optimize-main-{hz}-final2.json')==read(OUT/f'notify-dispatch-debug-main-{hz}-final2.json'),'Ordinary ten-role history differs')
            rows.append(dict(configuration=configuration,**row))
    require(read(OUT/'notify-dispatch-debug-input-final2.json')==read(OUT/'notify-dispatch-optimize-input-final2.json'),'Ordinary E differs between builds')
    for p in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
        require(sha(ROOT/'.godot/mono/temp/bin/Debug'/p)==sha(OUT/'notify-dispatch-debug-backup-final2'/p),'Debug restore failed')
    for p in ('notify-dispatch-build-debug-final2.log','notify-dispatch-build-optimize-final2.log'):
        log=(OUT/p).read_text(encoding='utf-8-sig')
        require(re.search(r'^\s*0\s*(?:个警告|Warning)',log,re.M) and re.search(r'^\s*0\s*(?:个错误|Error)',log,re.M),'Unclean build')
    protected={};packages={}
    for name in ('motion_warping_v1_policy.json','motion_warping_v1_usage.json','motion_warping_v1_game_feature.json','emote_v1_policy.json','notify_dispatch_v1_policy.json'):
        data=read(ASSETS/name)
        for p,d in data['previousFixtureSha256'].items():require(sha(ASSETS/p)==d,'Existing JSON changed: '+p);protected[p]=d
        for p,d in data['protectedProject'].items():require(sha(PROJECT/p)==d,'Original project/config changed')
        if isinstance(data['assetSha256'],dict):packages.update(data['assetSha256'])
        else:packages[data['path']]=data['assetSha256']
    require(len(protected)==857 and len(list(ASSETS.rglob('*.json')))==860,'Resource closure changed')
    for p,d in packages.items():require(sha(package_file(p))==d,'Original package changed: '+p)
    render=(OUT/'pivot-notify-render.stdout.log').read_text(encoding='utf-8-sig')+(OUT/'pivot-notify-render.stderr.log').read_text(encoding='utf-8-sig')
    require('LYRA_PIVOT_NOTIFY_PHYSICS_GODOT_OK hz=60' in render and not re.search(r'^\s*(?:ERROR|WARNING):',render,re.M),'Rendered Pivot failed')
    require(read(OUT/'pivot-notify-render-process.json')['exitCode']==0 and read(OUT/'pivot-notify-render.json')==read(OUT/'notify-dispatch-debug-60-final2.json'),'Rendered physical history differs')
    captures=[OUT/f'pivot-notify-render-{frame}.png' for frame in (31,151,271)]
    require(all(p.exists() and p.stat().st_size>10000 for p in captures),'Actual render captures missing')
    result=dict(scope=dict(originalGeneratedPivotRule=True,originalNotifyQueueHistory=True,controlledSourceContexts=True,
        originalEmptyTransitionStateLifecycle=True,namedExternalDispatchReference=True,namedProductionExternalHooks=False,
        mainSourceStateProduction=True,actualAlsSkin=True,actualPhysics=True,wholeMainNative=False,goalComplete=False),
        nativeFramesPerBuild=36920,nativeTraces=237,runs=rows,protectedJson=len(protected),protectedPackages=len(packages),
        actualGodotProcesses=23,actualUEProcesses=2,renderedFrames=480,captures=[dict(path=str(p.relative_to(ROOT)),sha256=sha(p)) for p in captures])
    result_path.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('LYRA_NOTIFY_DISPATCH_VERIFIED processes=23 nativeUE=2 nativeFrames=36920 protectedJson=857')
if __name__=='__main__':main()

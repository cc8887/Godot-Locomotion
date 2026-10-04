"""Audit named receiver reference, production matrices and immutable original assets."""
import hashlib
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/lyra-analysis'
ASSETS=ROOT/'assets/generated/lyra_als'
PROJECT=ROOT.parent/'GASP58'

def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def require(v,m):
    if not v:raise RuntimeError(m)
def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return PROJECT/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return PROJECT/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)

def main():
    result_path=OUT/'named-notify-verification.json';require(not result_path.exists(),'Preserve final audit')
    policy=read(ASSETS/'named_notify_v1_policy.json');native=read(ASSETS/'named_notify_v1_native.json')
    require(native['policySha256']==sha(ASSETS/'named_notify_v1_policy.json') and native['requestSha256']==sha(ASSETS/'named_notify_v1_requests.json'),'Changed reference')
    for p,d in policy['dependencies'].items():require(sha(ASSETS/p)==d,'Changed native dependency: '+p)
    for name in ('named-notify-native-first','named-notify-native-independent-final'):
        log=(OUT/(name+'.log')).read_text(encoding='utf-8-sig')
        require('LYRA_NAMED_NOTIFY_NATIVE_OK traces=55 frames=890 previous=860 packages=709 assets_saved=0' in log and 'NAMED_NOTIFY_PROCESS_EXIT=0' in log,'UE reference did not finish')
        require(not re.search(r'Error:|Ensure condition failed|Assertion failed|Fatal error|Traceback \(most recent',log),'UE reference failed')
    for p,d in policy['probeSourceSha256'].items():
        require(sha(ROOT/'tools/unreal/LyraNamedNotifyOracle'/p)==d and sha(ROOT/'artifacts/unreal/lyra-named-notify-oracle/package-nested'/p)==d,'Probe source differs from compiled package')
    require(not (PROJECT/'Plugins/LyraNamedNotifyOracle').exists(),'Optional probe left installed')
    rows=[]
    for configuration in ('debug','optimize'):
        log=(OUT/f'named-notify-{configuration}-final.log').read_text(encoding='utf-8-sig')
        exits=re.findall(r'LYRA_NAMED_NOTIFY_RUN_EXIT name=(\S+) code=(\d+)',log)
        require(len(exits)==15 and all(code=='0' for _,code in exits),'Fifteen-process matrix incomplete')
        require(not re.search(r'^\s*(?:ERROR|WARNING):',log,re.M),'Godot matrix issue')
        require('LYRA_NAMED_NOTIFY_MATRIX_OK' in log and 'traces=55 frames=890 retries=890 events=229' in log and
            'frames=36920 retries=36920 pivotTrue=8674 named=22 reachedEndHistory=201 inactiveHistory=1' in log,'Native receiver/rule gates missing')
        require('LYRA_EMOTE_PHYSICS_GODOT_OK' in log and 'LYRA_MOTION_WARPING_PHYSICS_GODOT_OK' in log and 'LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK' in log,'Prior physics gates missing')
        loaded=dict(re.findall(r'LYRA_NAMED_NOTIFY_ASSEMBLY file=(\S+) sha256=(\w+)',log))
        directory=ROOT/'.godot/mono/temp/bin'/('Debug' if configuration=='debug' else 'ExportRelease')
        require(len(loaded)==3,'Assembly identities missing')
        for p,d in loaded.items():require(sha(directory/p).upper()==d,'Matrix assembly changed')
        for hz in (30,60,120):
            row=read(OUT/f'named-notify-{configuration}-{hz}-final.json')
            require(row['roles']==6 and row['frames']==hz*4 and row['moves']==row['retries']==hz*24 and row['switches']==6,'Actual named role coverage missing')
            require(row['named']==40 and row['callbacks']==140 and row['lateBindings']>0 and row['rejections']>0 and row['actualAlsModel'] and row['actualJolt'],'Production named boundaries missing')
            if configuration=='optimize':
                require(row==read(OUT/f'named-notify-debug-{hz}-final.json'),'Optimized named callback history differs')
                for suffix in (f'pivot-{hz}',f'main-{hz}'):
                    require(read(OUT/f'named-notify-optimize-{suffix}-final.json')==read(OUT/f'named-notify-debug-{suffix}-final.json'),'Prior history differs between builds')
            rows.append(dict(configuration=configuration,**row))
        build=(OUT/f'named-notify-build-{configuration}-final.log').read_text(encoding='utf-8-sig')
        require(re.search(r'^\s*0\s*(?:个警告|Warning)',build,re.M) and re.search(r'^\s*0\s*(?:个错误|Error)',build,re.M),'Unclean build')
    require(read(OUT/'named-notify-debug-input-final.json')==read(OUT/'named-notify-optimize-input-final.json'),'Ordinary E differs')
    for p in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
        require(sha(ROOT/'.godot/mono/temp/bin/Debug'/p)==sha(OUT/'named-notify-debug-backup-final'/p),'Debug restore differs')
    for p,d in policy['previousFixtureSha256'].items():require(sha(ASSETS/p)==d,'Existing JSON changed: '+p)
    require(len(policy['previousFixtureSha256'])==860 and len(list(ASSETS.rglob('*.json')))==863,'Resource closure changed')
    for p,d in policy['protectedProject'].items():require(sha(PROJECT/p)==d,'Original project/config changed')
    for p,d in policy['assetSha256'].items():require(sha(package_file(p))==d,'Original package changed: '+p)
    render=(OUT/'named-notify-render.stdout.log').read_text(encoding='utf-8-sig')+(OUT/'named-notify-render.stderr.log').read_text(encoding='utf-8-sig')
    require('LYRA_NAMED_NOTIFY_PHYSICS_GODOT_OK hz=60' in render and not re.search(r'^\s*(?:ERROR|WARNING):',render,re.M),'Actual rendered named batch failed')
    require(read(OUT/'named-notify-render-process.json')['exitCode']==0 and read(OUT/'named-notify-render.json')==read(OUT/'named-notify-debug-60-final.json'),'Rendered callbacks differ')
    captures=[OUT/f'named-notify-render-{frame}.png' for frame in (31,151,211)]
    require(all(p.exists() and p.stat().st_size>10000 for p in captures),'Rendered captures missing')
    result=dict(scope=dict(originalNamedAssets=True,originalDelegateAndDispatcher=True,originalGeneratedClasses=True,
        controlledInstanceFunctions=True,controlledLinkedRoster=True,productionNamedExternalHooks=True,productionCommittedMainQueue=True,
        actualAlsSkin=True,actualPhysics=True,wholeMainNative=False,goalComplete=False),
        nativeTraces=55,nativeFramesPerBuild=890,nativeCallbacksPerBuild=229,runs=rows,
        protectedJson=860,resourceJsonTotal=863,protectedPackages=len(policy['assetSha256']),actualUEProcesses=2,actualGodotProcesses=31,
        renderedFrames=240,captures=[dict(path=str(p.relative_to(ROOT)),sha256=sha(p)) for p in captures])
    result_path.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('LYRA_NAMED_NOTIFY_VERIFIED processes=31 nativeUE=2 frames=890 callbacks=229 protectedJson=860')

if __name__=='__main__':main()

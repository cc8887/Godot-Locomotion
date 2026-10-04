"""Audit final Emote reference, actual process exits, resource closure and builds."""
import hashlib
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/lyra-analysis'
ASSETS=ROOT/'assets/generated/lyra_als'
PROJECT=ROOT.parent/'GASP58'
cache={}
def sha(p):
    if p not in cache:cache[p]=hashlib.sha256(p.read_bytes()).hexdigest()
    return cache[p]
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def require(value,message):
    if not value:raise RuntimeError(message)
def package_file(p):
    p=p.split('.')[0]
    if p.startswith('/Game/'):return PROJECT/'Content'/(p.removeprefix('/Game/')+'.uasset')
    if p.startswith('/ShooterCore/'):return PROJECT/'Plugins/GameFeatures/ShooterCore/Content'/(p.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(p)

def main():
    final=OUT/'emote-verification.json';require(not final.exists(),'Preserve final Emote verification')
    for name,marker in (('emote-native-first','LYRA_EMOTE_NATIVE_OK'),('emote-native-independent','LYRA_EMOTE_NATIVE_OK'),
                        ('emote-edges-first','LYRA_EMOTE_EDGES_OK'),('emote-edges-independent','LYRA_EMOTE_EDGES_OK')):
        log=(OUT/(name+'.log')).read_text(encoding='utf-8-sig')
        require(marker in log and 'EMOTE_PROCESS_EXIT=0' in log,'Original GA process did not complete')
        require(not re.search(r'Error:|Ensure condition failed|Assertion failed|Fatal error|Traceback \(most recent',log),'Original GA reference contains a failure')
    policy=read(ASSETS/'emote_v1_policy.json')
    for stem in ('emote_v1','emote_edges_v1'):
        native=read(ASSETS/(stem+'_native.json'))
        require(native['policySha256']==sha(ASSETS/'emote_v1_policy.json') and native['requestSha256']==sha(ASSETS/(stem+'_requests.json')),'Stale original Emote closure')
    rows=[]
    baseline=read(OUT/'root-movement-debug-main-final6.json')
    for configuration in ('debug','optimize'):
        log=(OUT/f'emote-{configuration}-verified.log').read_text(encoding='utf-8-sig')
        exits=re.findall(r'LYRA_EMOTE_RUN_EXIT name=(\S+) code=(\d+)',log)
        require(len(exits)==8 and all(code=='0' for _,code in exits),'Final eight-process matrix incomplete')
        require(not re.search(r'^\s*(?:ERROR|WARNING):',log,re.M),'Godot issue in final Emote matrix')
        require('LYRA_EMOTE_MATRIX_OK' in log and 'traces=54 frames=27720 retries=27720 checks=1774080' in log,'Original generated ability model not verified')
        require('LYRA_MOTION_WARPING_PHYSICS_GODOT_OK' in log and 'LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK' in log,'Prior physical movement regressions missing')
        loaded=dict(re.findall(r'LYRA_EMOTE_ASSEMBLY file=(\S+) sha256=(\w+)',log))
        directory=ROOT/'.godot/mono/temp/bin'/('Debug' if configuration=='debug' else 'ExportRelease')
        require(len(loaded)==3,'Actual loaded assemblies missing')
        for name,value in loaded.items():require(sha(directory/name).upper()==value,'Final matrix loaded a different assembly')
        require(read(OUT/f'emote-{configuration}-main-verified.json')==baseline,'Ordinary ten-character baseline changed')
        input_report=read(OUT/f'emote-{configuration}-input-verified.json')
        require(input_report['emote']==dict(Activations=1,Ends=1,MovementClears=1),'Ordinary E input did not reach original GA cancellation')
        for hz in (30,60,120):
            row=read(OUT/f'emote-{configuration}-{hz}-verified.json')
            require(row['moves']==row['preRetries']==row['animationRetries']==hz*48,'Single physical movement / cancellation retries incomplete')
            require(row['roles']==6 and row['frames']==hz*8 and row['activations']==7 and row['ends']==6 and row['clears']==3,'Actual physical Emote scenario missing')
            require(row['actualJolt'] and row['actualAlsModel'] and not row['automaticMotionWarping'] and not row['wholeMainNative'],'Physics verification scope changed')
            if configuration=='optimize':require(row==read(OUT/f'emote-debug-{hz}-verified.json'),'Optimize physical history differs')
            rows.append(dict(configuration=configuration,**row))
    require(read(OUT/'emote-debug-input-verified.json')==read(OUT/'emote-optimize-input-verified.json'),'Ordinary input histories differ between builds')
    for name in ('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb'):
        require(sha(ROOT/'.godot/mono/temp/bin/Debug'/name)==sha(OUT/'emote-debug-backup-verified'/name),'Debug assembly restore failed')
    trx=ET.parse(OUT/'emote-core-final.trx').getroot();counts=next(x.attrib for x in trx.iter() if x.tag.endswith('Counters'))
    require(counts['total']==counts['passed']=='525' and counts['failed']=='0','Core boundary tests failed')
    protected={};packages={}
    for name in ('motion_warping_v1_policy.json','motion_warping_v1_usage.json','motion_warping_v1_game_feature.json','emote_v1_policy.json'):
        data=read(ASSETS/name)
        for p,d in data['previousFixtureSha256'].items():require(sha(ASSETS/p)==d,'Existing JSON changed: '+p);protected[p]=d
        for p,d in data['protectedProject'].items():require(sha(PROJECT/p)==d,'Original project/configuration changed')
        if isinstance(data['assetSha256'],dict):packages.update(data['assetSha256'])
        else:packages[data['path']]=data['assetSha256']
    require(len(protected)==852 and len(list(ASSETS.rglob('*.json')))==857,'Emote resource closure changed')
    for p,d in packages.items():require(sha(package_file(p))==d,'Original package changed: '+p)
    for p,d in policy['probeSourceSha256'].items():
        require(sha(ROOT/'tools/unreal/LyraEmoteOracle'/p)==d,'Reference probe source changed')
        require(sha(OUT.parent/'unreal/lyra-emote-oracle/package-ready'/p)==d,'Reference probe package differs')
    require(not (PROJECT/'Plugins/LyraEmoteOracle').exists(),'Temporary Emote probe left installed')
    render_log=(OUT/'emote-render.stdout.log').read_text(encoding='utf-8-sig')+(OUT/'emote-render.stderr.log').read_text(encoding='utf-8-sig')
    require('LYRA_EMOTE_PHYSICS_GODOT_OK hz=60' in render_log and not re.search(r'^\s*(?:ERROR|WARNING):',render_log,re.M),'Rendered Emote failed')
    require(read(OUT/'emote-render-process.json')['exitCode']==0 and read(OUT/'emote-render.json')==read(OUT/'emote-debug-60-verified.json'),'Rendered physical history differs')
    captures=[OUT/f'emote-render-{frame}.png' for frame in (31,91,271)]
    require(all(p.exists() and p.stat().st_size>10000 for p in captures),'Actual Emote render captures missing')
    result=dict(scope=dict(originalGeneratedGAExecutedInUE=True,originalLyraASCAndTask=True,controlledMovementAndUncrouchReference=True,
        localGodotAbilityModelMatched=True,actualAlsSkin=True,actualPhysics=True,ordinaryEInput=True,
        originalAfterBlendOutInterruptionPreserved=True,resourceNotifyDispatchInReference=False,wholeMainNative=False,fullGASPort=False,goalComplete=False),
        runs=rows,originalNativeFramesPerBuild=27720,originalNativeChecksPerBuild=1774080,corePassed=525,
        protectedJson=len(protected),protectedPackages=len(packages),actualGodotProcesses=17,actualUEProcesses=4,renderedFrames=480,
        captures=[dict(path=str(p.relative_to(ROOT)),sha256=sha(p)) for p in captures])
    final.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('LYRA_EMOTE_VERIFIED processes=17 nativeUE=4 builds=2 nativeFrames=27720 core=525')

if __name__=='__main__':main()

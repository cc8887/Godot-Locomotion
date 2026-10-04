"""Read-only execution of original GA_Emote, Lyra ASC and PlayMontageAndWait."""
import hashlib
import json
from pathlib import Path
import unreal

repo = Path(__file__).resolve().parents[2]
root = repo / 'assets/generated/lyra_als'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
load = lambda p: json.loads(p.read_bytes())
names = {'emote_v1_policy.json', 'emote_v1_requests.json', 'emote_v1_native.json'}
previous = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*.json') if p.name not in names}
project = Path(unreal.Paths.get_project_file_path())
protected = {p.relative_to(project.parent).as_posix(): sha(p) for p in [project, *(project.parent/'Config').rglob('*.ini')]}
packages = dict(load(root/'motion_warping_v1_policy.json')['assetSha256'])
probe = repo/'tools/unreal/LyraEmoteOracle'
sources = {p.relative_to(probe).as_posix(): sha(p) for p in probe.rglob('*') if p.is_file()}

def package_file(path):
    path = path.split('.')[0]
    if path.startswith('/Game/'): return project.parent/'Content'/(path.removeprefix('/Game/')+'.uasset')
    if path.startswith('/ShooterCore/'): return project.parent/'Plugins/GameFeatures/ShooterCore/Content'/(path.removeprefix('/ShooterCore/')+'.uasset')
    raise ValueError(path)

def protect():
    for p, d in previous.items(): assert sha(root/p) == d, p
    for p, d in protected.items(): assert sha(project.parent/p) == d, p
    for p, d in packages.items(): assert sha(package_file(p)) == d, p
    for p, d in sources.items(): assert sha(probe/p) == d, p

def write(name, data):
    p = root/name
    if p.exists(): assert load(p) == data, 'Independent Emote reference changed: '+name
    else:
        with p.open('x', encoding='utf-8', newline='\n') as f:
            f.write(json.dumps(data, ensure_ascii=False, separators=(',', ':'), allow_nan=False)+'\n')
    return sha(p)

protect()
policy = json.loads(unreal.LyraEmoteOracleLibrary.read_policy())
traces = []
modes = ('complete', 'moving', 'tiny', 'vertical', 'move_late', 'uncrouch', 'held_crouch',
         'cancel', 'interrupt', 'complete_then_move', 'repeat', 'no_movement')
for hz in (30, 60, 120):
    for mode in modes:
        frames = []
        # Repeat traverses two complete instances of the original 5.45s montage.
        for i in range(hz*(13 if mode == 'repeat' else 7)):
            f = dict(delta=1/hz, crouched=False, activate=i == 0, cancel=False,
                     interrupt=False, applyUncrouch=True, movement=mode != 'no_movement', oldVelocity=[0, 0, 0])
            if mode == 'moving': f['oldVelocity'] = [1, 0, 0]
            if mode == 'tiny': f['oldVelocity'] = [1e-24, 0, 0]
            if mode == 'vertical': f['oldVelocity'] = [0, 0, -1]
            if mode == 'move_late' and i >= hz//5: f['oldVelocity'] = [50, 0, 0]
            if mode == 'uncrouch': f['crouched'] = i == 0
            if mode == 'held_crouch': f['crouched'] = True; f['applyUncrouch'] = False
            if mode == 'cancel' and i == hz//5: f['cancel'] = True
            if mode == 'interrupt' and i == hz//5: f['interrupt'] = True
            if mode == 'complete_then_move' and i >= hz*6: f['oldVelocity'] = [1, 0, 0]
            if mode == 'repeat' and i in (hz//5, hz*6): f['activate'] = True
            frames.append(f)
        traces.append(dict(hz=hz, mode=mode, frames=frames))
request = dict(schemaVersion=1, mesh='/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny.SKM_Manny',
    montage='/Game/Characters/Heroes/Mannequin/Animations/Actions/AM_MF_Emote_FingerGuns_Emote_MW.AM_MF_Emote_FingerGuns_Emote_MW',
    otherMontage='/Game/Weapons/Pistol/Animations/AM_MM_Pistol_Reload.AM_MM_Pistol_Reload', traces=traces)
native = json.loads(unreal.LyraEmoteOracleLibrary.read_trace(json.dumps(request, separators=(',', ':'))))
assert len(native['traces']) == len(traces) == 36
for t, n in zip(traces, native['traces'], strict=True): assert len(t['frames']) == len(n['frames'])
protect()
policy_sha = write('emote_v1_policy.json', dict(schemaVersion=1, engineVersion=unreal.SystemLibrary.get_engine_version(),
    dependencies={p: sha(root/p) for p in ('motion_warping_v1_policy.json', 'montage_catalog_v2.json')},
    previousFixtureSha256=previous, protectedProject=protected, assetSha256=packages, probeSourceSha256=sources, trace=policy))
request_sha = write('emote_v1_requests.json', request)
write('emote_v1_native.json', dict(schemaVersion=1, policySha256=policy_sha, requestSha256=request_sha, trace=native,
    scope=dict(originalGeneratedAbility=True, originalLyraASC=True, originalAbilityTask=True,
        originalMontages=True, controlledMovementCallback=True, controlledUncrouchApplication=True,
        resourceNotifyDispatch=False, wholeWorldPhysics=False, wholeMainEvaluated=False, assetsSaved=0)))
unreal.log('LYRA_EMOTE_NATIVE_OK traces='+str(len(traces))+' frames='+str(sum(len(t['frames']) for t in traces))+
           ' previous='+str(len(previous))+' packages='+str(len(packages))+' assets_saved=0')

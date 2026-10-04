"""Check the exact missing-source extension and its immutable ALS provenance."""
import argparse
import hashlib
import json
from pathlib import Path

from locomotion_paths import engine_path, project_path

sha=lambda data:hashlib.sha256(data).hexdigest()
def verify(root,content,engine_content):
    directory=root/'locomotion_extras'
    load=lambda path:json.loads(path.read_bytes())
    catalog=load(directory/'catalog.json');inventory=load(root/'locomotion_layer_closures.json')
    catalog_sha=sha((directory/'catalog.json').read_bytes())
    for name,digest in catalog['dependencies'].items():
        if sha((root/name).read_bytes())!=digest:raise ValueError('Changed extension dependency '+name)
    if catalog['schemaVersion']!=1 or catalog['skinPreservation']!=0:raise ValueError('Changed extension profile')
    entries={r['slot']:r for r in catalog['entries']}
    if len(entries)!=8 or len(catalog['entries'])!=8 or sum(r['additive'] for r in entries.values())!=3:
        raise ValueError('Incomplete exact resource gap set')
    if sorted(r['source'] for r in entries.values())!=inventory['missingSequences']:
        raise ValueError('Extension does not close the actual original resource gaps')
    curves={r['slot']:r for r in catalog['curves']}
    if set(curves)!=set(entries):raise ValueError('Incomplete source curve/attribute inventory')
    policies={}
    for slot,row in entries.items():
        file=directory/row['file']
        if not file.resolve().is_relative_to((directory/'clips').resolve()) or sha(file.read_bytes())!=row['sha256']:
            raise ValueError('Changed extra raw resource '+slot)
        clip=load(file);metadata=clip['metadata'];curve=curves[slot]
        if clip['source']!=row['source'] or clip['target']!=row['target'] or clip['raw']['source']!=row['target'] or \
            clip['raw']['sampledKeyCount']!=row['keyCount'] or clip['raw']['playLength']!=row['playLength']:
            raise ValueError('Changed extra identity/time '+slot)
        base_time=0
        if row['additive']:
            profile=row['profile'];old=load(root/(profile+'_jump_additive_catalog.json'))['clips'][0]
            for field in ('source','target'):
                if row[field]!=old[field]:raise ValueError('Changed existing Jump Recovery binding')
            for field in ('additiveType','basePoseType','baseFrame','baseAsset'):
                if metadata[field]!=old['metadata'][field]:raise ValueError('Changed actual Jump Recovery policy')
            if metadata['additiveType']!='AAT_LocalSpaceBase' or row['baseSlot']!=slot:raise ValueError('Wrong local additive base')
            if row['sequencePlayLength']!=metadata['sequencePlayLength']:raise ValueError('Changed original sequence length')
            base_time=row['sequencePlayLength']*min(1,max(0,metadata['baseFrame']/row['keyCount']))
            policies[profile]={'type':metadata['basePoseType'],'frame':metadata['baseFrame'],'seconds':base_time}
        elif metadata['additiveType']!='AAT_None' or row['baseSlot'] is not None:
            raise ValueError('Idle Break unexpectedly additive')
        if row['baseSampleTime']!=base_time or curve['baseSampleTime']!=base_time or curve['baseSlot']!=row['baseSlot']:
            raise ValueError('Wrong original frame-base time')
        if len(curve['attributes'])!=metadata['animatedBoneAttributeCount'] or curve['transformCurves']!=0 or \
            [c['name'] for c in curve['curves']]!=metadata['floatCurveNames']:
            raise ValueError('Source metadata discarded')
    native=load(directory/'native.json');bindings=load(directory/'bindings.json');playback=load(directory/'playback.json')
    for value in (native,bindings,playback):
        if value['schemaVersion']!=1 or value['catalogSha256']!=catalog_sha:raise ValueError('Stale extension output')
    if len(native['rows'])!=475 or len(native['curveRows'])!=len(native['rows']) or \
        {r['slot'] for r in native['rows']}!=set(entries) or {r['slot'] for r in playback['entries']}!=set(entries):
        raise ValueError('Incomplete native/source playback coverage')
    for row in playback['entries']:
        if row['sourceSync']['markers']!=row['targetSync']['markers'] or \
            row['sourceSync']['rateScale']!=row['targetSync']['rateScale'] or \
            row['sourceNotifies']['events']!=row['targetNotifies']['events']:
            raise ValueError('Retarget changed source markers, rate or typed Notify payloads')
    for row in native['rows']:
        if len(row['raw'])!=81 or len(row['output'])!=81:raise ValueError('Incomplete logical pose')
    if bindings['inventorySha256']!=sha((root/'locomotion_layer_closures.json').read_bytes()) or bindings['missingSequences']:
        raise ValueError('Stale or incomplete default binding closure')
    if set(bindings['providers'])!=set(inventory['providers']):raise ValueError('Unknown provider')
    by_source={r['source']:r for r in entries.values()};sequence_bindings=0
    for profile,provider in inventory['providers'].items():
        actual=bindings['providers'][profile]
        expected={source:targets or [by_source[source]['target']] for source,targets in provider['sequenceTargets'].items()}
        if actual!=expected:raise ValueError('Changed original ordered sequence targets '+profile)
        sequence_bindings+=len(actual)
    packages=catalog['assetSha256']
    if not inventory['assetSha256'].items()<=packages.items():raise ValueError('Lost old package protection')
    for path,digest in packages.items():
        stem=path.split('.')[0];directory_mount=content if stem.startswith('/Game/') else engine_content if stem.startswith('/Engine/') else None
        if directory_mount is None or sha((directory_mount/(stem.split('/',2)[2]+'.uasset')).read_bytes())!=digest:
            raise ValueError('Changed protected UE package '+path)
    old_catalog=load(root/'logical_controls/catalog.json')
    for entry in old_catalog['entries']:
        if sha((root/'logical_controls'/entry['file']).read_bytes())!=entry['sha256']:
            raise ValueError('Changed existing logical source '+entry['slot'])
    return {'status':'pass_resources','extras':8,'idleBreaks':5,'jumpAdditives':3,'nativeSamples':len(native['rows']),
        'curveRows':len(native['curveRows']),'packages':len(packages),'oldPackages':len(inventory['assetSha256']),
        'oldLogicalSources':len(old_catalog['entries']),'logical':81,'skin':68,'skinPreservation':0,
        'additivePolicies':policies,'providers':3,'sequenceBindings':sequence_bindings,'missingSequences':0,
        'runtime':False,'production':False,'files':{str(p.relative_to(directory)):{'sha256':sha(p.read_bytes()),'bytes':p.stat().st_size}
            for p in sorted(directory.rglob('*.json'))}}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=project_path('Content'))
    parser.add_argument('--engine-content',type=Path,default=engine_path('Engine/Content'))
    parser.add_argument('--output',type=Path,default=Path('artifacts/lyra-analysis/idle-recovery-resource-verification.json'))
    args=parser.parse_args();report=verify(args.root,args.content,args.engine_content)
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LYRA_IDLE_RECOVERY_VERIFY_OK '+json.dumps({k:v for k,v in report.items() if k!='files'}))

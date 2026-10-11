"""Bake the pipeline's final full-pose raw into FBX and verify native readback.

KIMODO_FINAL_TRACKS_REQUEST/OUTPUT select a fresh request and receipt. Source
animations are read only; scratch copies live under a unique version subtree.
"""
import gc
import hashlib
import json
import math
import os
from pathlib import Path
import unreal


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def difference(actual, positions, rotations, scales, frame):
    p=positions[min(frame,len(positions)-1)];q=rotations[min(frame,len(rotations)-1)]
    s=scales[min(frame,len(scales)-1)]
    a=actual['positions'][min(frame,len(actual['positions'])-1)]
    b=actual['rotations'][min(frame,len(actual['rotations'])-1)]
    c=actual['scales'][min(frame,len(actual['scales'])-1)] if actual['scales'] else [1,1,1]
    dot=abs(sum(x*y for x,y in zip(b,q)))
    norm=math.sqrt(sum(x*x for x in b)*sum(x*x for x in q))
    return (math.dist(a,p),math.degrees(2*math.acos(min(1.,dot/norm))),math.dist(c,s))


def source_data(animation):
    text=unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(animation)
    if not text:raise RuntimeError('Could not read the native animation DataModel')
    return json.loads(text,parse_int=lambda value:-0.0 if value=='-0' else int(value))


def finish_compilation():
    # Registered by FAssetCompilingManager. Kismet routes this directly through
    # IConsoleManager, including commandlets without a world or player.
    # Settle data-controller/import tasks before editing or releasing copies;
    # otherwise UE 5.8 can requeue the same compression hash during shutdown.
    unreal.SystemLibrary.execute_console_command(None,'Editor.AsyncAssetCompilationFinishAll')


def main():
    request_path=Path(os.environ['KIMODO_FINAL_TRACKS_REQUEST']).resolve()
    output=Path(os.environ['KIMODO_FINAL_TRACKS_OUTPUT']).resolve()
    if output.exists():raise ValueError('Final FBX receipt exists; preserve it and use a fresh run')
    request=json.loads(request_path.read_bytes());version=request['version']
    if not version.replace('_','').isalnum():raise ValueError('Unsafe final bake version')
    project=Path(__file__).resolve().parents[2]
    if request.get('pack'):
        pointer=project/'assets/config/als_resource_pack.json'
        if pointer.exists() and json.loads(pointer.read_bytes()).get('pack')==request['pack']:
            raise ValueError('Cannot overwrite FBX in the active resource pack')
    directory='/Game/KimodoMocara/FinalTracks/'+version
    unreal.EditorAssetLibrary.make_directory(directory)
    completed=[]
    for job in request['jobs']:
        if sha(job['raw'])!=job['rawSha256']:raise ValueError('Final raw changed: '+job['source'])
        raw=json.loads(Path(job['raw']).read_bytes());count=raw['sampledKeyCount']
        rate=raw['frameRateNumerator']/raw['frameRateDenominator']
        if count<2 or abs((count-1)/rate-raw['playLength'])>1e-5:
            raise ValueError('Final raw must preserve a uniform native time domain')
        if abs(rate-round(rate))>1e-8:
            raise ValueError('Native FBX custom sample rate cannot preserve this fractional rate')
        source=unreal.load_asset(job['sourceAsset'])
        if not isinstance(source,unreal.AnimSequence):raise RuntimeError('Missing native source animation')
        identifier=hashlib.sha256(job['source'].encode()).hexdigest()[:20]
        scratch=directory+'/AS_'+identifier
        if unreal.EditorAssetLibrary.does_asset_exist(scratch):raise ValueError('Scratch animation exists; use a fresh version')
        anim=unreal.EditorAssetLibrary.duplicate_asset(job['sourceAsset'],scratch)
        if not isinstance(anim,unreal.AnimSequence):raise RuntimeError('Could not copy native animation')
        finish_compilation()
        skeleton=source.get_editor_property('skeleton')
        controller=anim.controller
        controller.open_bracket('Bake final Kimodo trajectory ownership',False)
        try:
            controller.set_frame_rate(unreal.FrameRate(numerator=raw['frameRateNumerator'],denominator=raw['frameRateDenominator']),False)
            controller.set_number_of_frames(unreal.FrameNumber(value=count-1),False)
            for track in raw['tracks']:
                def expand(field,width):
                    values=track[field]
                    if len(values) not in (1,count) or any(len(v)!=width for v in values):
                        raise ValueError('Invalid final track '+track['bone']+'/'+field)
                    return values*count if len(values)==1 else values
                positions=[unreal.Vector(*v) for v in expand('positions',3)]
                rotations=[unreal.Quat(*v) for v in expand('rotations',4)]
                scales=[unreal.Vector(*v) for v in expand('scales',3)]
                if not controller.set_bone_track_keys(track['bone'],positions,rotations,scales,False):
                    raise RuntimeError('Could not author final track '+track['bone'])
        finally:controller.close_bracket(False)
        unreal.AnimationLibrary.set_root_motion_enabled(anim,False)
        anim.set_editor_property('force_root_lock',False)
        finish_compilation()
        if not unreal.EditorAssetLibrary.save_loaded_asset(anim,only_if_is_dirty=False):raise RuntimeError('Could not save scratch animation')
        fbx=Path(job['fbx']).resolve();fbx.parent.mkdir(parents=True,exist_ok=True)
        if request.get('pack'):
            pack_root=(project/request['pack'].removeprefix('res://')).parent.resolve()
            if not fbx.is_relative_to(pack_root):raise ValueError('Foreign staged FBX output')
        elif fbx.exists():raise ValueError('Standalone FBX output already exists')
        options=unreal.FbxExportOption()
        options.set_editor_property('ascii',True)
        options.set_editor_property('map_skeletal_motion_to_root',False)
        task=unreal.AssetExportTask();task.object=anim;task.exporter=unreal.AnimSequenceExporterFBX()
        task.filename=str(fbx);task.options=options;task.automated=True;task.prompt=False;task.replace_identical=True
        if not unreal.Exporter.run_asset_export_task(task) or not fbx.is_file():raise RuntimeError('Final FBX export failed')
        ui=unreal.FbxImportUI();ui.set_editor_property('mesh_type_to_import',unreal.FBXImportType.FBXIT_ANIMATION)
        ui.set_editor_property('import_as_skeletal',True);ui.set_editor_property('import_mesh',False)
        ui.set_editor_property('import_animations',True);ui.set_editor_property('skeleton',skeleton)
        ui.set_editor_property('automated_import_should_detect_type',False)
        # Preserve the original key domain; never silently round fractional FPS.
        ui.anim_sequence_import_data.set_editor_property('use_default_sample_rate',False)
        ui.anim_sequence_import_data.set_editor_property('custom_sample_rate',round(rate))
        ui.anim_sequence_import_data.set_editor_property('preserve_local_transform',True)
        ui.anim_sequence_import_data.set_editor_property('animation_length',unreal.FBXAnimationLengthImportType.FBXALIT_SET_RANGE)
        ui.anim_sequence_import_data.set_editor_property('frame_import_range',unreal.Int32Interval(min=0,max=count-1))
        imported=unreal.AssetImportTask();imported.filename=str(fbx);imported.destination_path=directory+'/Readback'
        # Explicit factory avoids Interchange converting a tiny 60 FPS pose
        # asset to a 30 FPS payload with no bone tracks. Scope is this task.
        imported.factory=unreal.FbxFactory()
        imported.destination_name='AS_'+identifier;imported.options=ui;imported.automated=True;imported.save=False
        unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([imported])
        paths=list(imported.imported_object_paths)
        if len(paths)!=1:raise RuntimeError('Final FBX readback did not produce one animation')
        readback=unreal.load_asset(paths[0]);readback.set_editor_property('force_root_lock',False)
        unreal.AnimationLibrary.set_root_motion_enabled(readback,False)
        finish_compilation()
        time_matches=(unreal.AnimationLibrary.get_num_keys(readback)==count and
            abs(unreal.AnimationLibrary.get_sequence_length(readback)-raw['playLength'])<1e-5)
        # Read the exact DataModel channels used by the native FBX exporter
        # and Godot raw sampler. Pose evaluation can retarget translations,
        # apply additive layers or recompute virtual bones even in Source mode.
        authored_data=source_data(anim);readback_data=source_data(readback)
        authored_tracks={t['bone'].lower():t for t in authored_data['tracks']}
        readback_tracks={t['bone'].lower():t for t in readback_data['tracks']}
        for data in (authored_data,readback_data):
            time_matches=time_matches and (data['sampledKeyCount']==count and
                abs(data['playLength']-raw['playLength'])<1e-5 and
                abs(data['frameRateNumerator']/data['frameRateDenominator']-rate)<1e-8)
        peak=[0.,0.,0.]
        peaks_at=[None,None,None];before_export_peak=[0.,0.,0.]
        for frame in range(count):
            for track in raw['tracks']:
                original=authored_tracks[track['bone'].lower()]
                authored_error=difference(original,track['positions'],track['rotations'],track['scales'],frame)
                before_export_peak=[max(a,b) for a,b in zip(before_export_peak,authored_error)]
                actual=readback_tracks[track['bone'].lower()]
                error=difference(actual,track['positions'],track['rotations'],track['scales'],frame)
                for index,value in enumerate(error):
                    if value>peak[index]:peaks_at[index]=dict(bone=track['bone'],frame=frame,error=value,
                        actualPositionCm=actual['positions'][min(frame,len(actual['positions'])-1)],
                        expectedPositionCm=track['positions'][min(frame,len(track['positions'])-1)])
                peak=[max(a,b) for a,b in zip(peak,error)]
        passed=time_matches and all(value<=limit for values in (peak,before_export_peak)
                                   for value,limit in zip(values,(.01,.1,1e-4)))
        saved=dict(source=job['source'],raw=job['raw'],rawSha256=sha(job['raw']),
            fbx=str(fbx),fbxSha256=sha(fbx),scratchAnimation=anim.get_path_name(),
            maximumPositionErrorCm=peak[0],maximumRotationErrorDegrees=peak[1],maximumScaleError=peak[2],
            timeDomainMatches=time_matches,sampledKeyCount=count,passed=passed,
            readbackPeaks=peaks_at,beforeExportMaximumErrors=before_export_peak,
            readbackPolicy='native-DataModel-physical-tracks',
            authoredTiming={k:authored_data[k] for k in ('sampledKeyCount','playLength','frameRateNumerator','frameRateDenominator')},
            readbackTiming={k:readback_data[k] for k in ('sampledKeyCount','playLength','frameRateNumerator','frameRateDenominator')})
        completed.append(saved)
        Path(str(fbx)+'.verification.json').write_text(json.dumps(saved,indent=2)+'\n',encoding='utf-8')
        output.parent.mkdir(parents=True,exist_ok=True)
        progress=output.with_suffix('.progress.json')
        progress.write_text(json.dumps(dict(requestSha256=sha(request_path),jobs=completed),indent=2)+'\n',encoding='utf-8')
        if not passed:raise RuntimeError('Final FBX round trip failed: '+json.dumps(saved))
        unreal.log('KIMODO_FINAL_TRACKS_JOB_OK '+job['source'])
        del controller,source,anim,readback,task,imported,ui,options
        gc.collect()
    finish_compilation()
    output.write_text(json.dumps(dict(schemaVersion=1,requestSha256=sha(request_path),jobs=completed),indent=2)+'\n',encoding='utf-8')
    unreal.log('KIMODO_FINAL_TRACKS_OK jobs='+str(len(completed)))


main()

"""Evaluate the exact authored V4/Refactored fixed Plant samples; no asset writes."""
import json
import math
import os
import time
from pathlib import Path
import unreal

request_path = Path(os.environ['ALS_STOP_PLANT_REQUEST'])
output = Path(os.environ['ALS_STOP_PLANT_OUTPUT'])
if not request_path.is_absolute() or not output.is_absolute() or output.exists():
    raise ValueError('Existing absolute request and fresh absolute output required')
request = json.loads(request_path.read_text(encoding='utf-8'))
if request['schemaVersion'] != 1 or len(request['samples']) != 24:
    raise ValueError('Expected both authored six-direction plants in both versions')
options = unreal.AnimPoseEvaluationOptions()
options.evaluation_type = unreal.AnimDataEvalType.RAW
options.should_retarget = True
options.extract_root_motion = False
options.incorporate_root_motion_into_pose = False
options.retrieve_additive_as_full_pose = True
options.evaluate_curves = True
results = []
for sample in request['samples']:
    sequence = unreal.load_asset(sample['source'])
    if not isinstance(sequence, unreal.AnimSequence):
        raise RuntimeError('Missing authored plant source: '+sample['source'])
    model = sequence.get_editor_property('data_model_interface')
    rate = model.get_frame_rate()
    seconds = sample['sample'] * rate.denominator / rate.numerator if sample['mode'] == 'frame' else sample['sample']
    if not 0 <= seconds <= model.get_play_length():
        raise ValueError('Authored sample outside source length')
    pose = unreal.AnimPoseExtensions.get_anim_pose_at_time(sequence, seconds, options)
    if not unreal.AnimPoseExtensions.is_valid(pose):
        raise RuntimeError('Invalid native plant pose')
    names = unreal.AnimPoseExtensions.get_bone_names(pose)
    def atom(name, space):
        t = unreal.AnimPoseExtensions.get_bone_pose(pose, name, space)
        p, q, s = t.translation, t.rotation, t.scale3d
        values = dict(position=[p.x,p.y,p.z], rotation=[q.x,q.y,q.z,q.w], scale=[s.x,s.y,s.z])
        if not all(math.isfinite(v) for field in values.values() for v in field):
            raise RuntimeError('Nonfinite authored pose')
        return values
    results.append(dict(input=sample, seconds=seconds, frameRate=dict(numerator=rate.numerator,denominator=rate.denominator),
        bones=[dict(name=str(n),local=atom(n,unreal.AnimPoseSpaces.LOCAL),component=atom(n,unreal.AnimPoseSpaces.WORLD)) for n in names],
        curves={str(n):unreal.AnimPoseExtensions.get_curve_weight(pose,n) for n in unreal.AnimPoseExtensions.get_curve_names(pose)}))
output.write_text(json.dumps(dict(schemaVersion=1,source='UE RAW exact authored Plant samples; not final graph or physical contact',samples=results),indent=2,allow_nan=False)+'\n',encoding='utf-8')
unreal.log(f'ALS_STOP_PLANT_CONTRACT_OK samples={len(results)} assets_saved=0 output={output}')
if os.environ.get('ALS_STOP_PLANT_QUIT') == '1':
    _deadline = time.monotonic()+10
    def _finish(_delta):
        if time.monotonic() >= _deadline:
            unreal.unregister_slate_post_tick_callback(_handle)
            unreal.SystemLibrary.quit_editor()
    _handle = unreal.register_slate_post_tick_callback(_finish)

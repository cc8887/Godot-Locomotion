"""Compile the checked-in ALS contracts into a deterministic authoring DAG.

The DAG includes explicit external inputs for undefined pose-table slots/prop rigs.
It never guesses the meaning of an unused Pose index from an asset name.
"""
from __future__ import annotations

import hashlib
import json
import re
from collections import Counter
from pathlib import Path


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False,
                                     separators=(',', ':')).encode()).hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def load_rules(path, seen=None):
    """Allow a focused acceptance profile to inherit the existing production rules."""
    path = Path(path).resolve()
    seen = set() if seen is None else set(seen)
    if path in seen:
        raise ValueError('Rules inheritance cycle: ' + str(path))
    seen.add(path)
    rules = read_json(path)
    parent = rules.pop('extends', None)
    return {**load_rules(path.parent / parent, seen), **rules} if parent else rules


def topological_order(nodes, targets=None):
    order, visiting, done = [], set(), set()

    def visit(key):
        if key not in nodes:
            raise ValueError(f'Missing dependency: {key}')
        if key in visiting:
            raise ValueError(f'Dependency cycle at {key}')
        if key in done:
            return
        visiting.add(key)
        for dep in sorted(nodes[key]['deps']):
            visit(dep)
        visiting.remove(key)
        done.add(key)
        order.append(key)

    for key in sorted(nodes if targets is None else targets):
        visit(key)
    return order


def target_origin_reference(row, rows):
    """Reference-pose origins follow the same master/base as their body pose.

    Right gait references use the right gait master, rather than copying a
    left origin without applying the mirror's target-space reflection.
    """
    family, name = row['family'], row['semanticName']
    by_name = {r['semanticName']: r for r in rows if r['edition'] == row['edition']}
    if family == 'gait_pose' or family == 'base_pose' and name == 'Lean_Pose':
        parent = 'Run_Forward' if name == 'Lean_Pose' else name.removesuffix('_Pose')
        if parent not in by_name:
            parent += '_Forward'
        if parent not in by_name:
            raise ValueError('Missing target origin gait master: ' + name)
        return dict(source=by_name[parent]['source'], fraction=.25, kind='gait-master-sample')
    if family in ('lean_additive', 'acceleration_additive', 'look_grid'):
        ev = row['originalEvaluation']
        if not ev['baseAsset']:
            raise ValueError('Missing target origin additive base: ' + name)
        return dict(source=ev['baseAsset'], frame=ev['baseFrame'], kind='native-additive-base')
    if family == 'pose_table_copy':
        parent = name.removesuffix('_NoCurves')
        if parent not in by_name:
            raise ValueError('Missing target origin pose table: ' + name)
        return dict(source=by_name[parent]['source'], frame=0, kind='pose-table-copy')
    return None


def build_graph(contracts, rules):
    if rules.get('schemaVersion')!=1 or rules['fps']!=30 or not 2<=rules['candidateSeconds']<=10:
        raise ValueError('Expected schema 1, SOMA 30 FPS and candidate duration 2–10 seconds')
    rows = contracts['assets']
    by_source = {r['source']: r for r in rows}
    by_name = {}
    for row in sorted(rows, key=lambda r: (r['edition'] != 'refactored', r['source'])):
        by_name.setdefault(row['semanticName'], row)
    nodes = {}
    gait_closure = rules.get('gaitClosure', {})
    allowed_gaits = {name for name, row in by_name.items() if row['family'] == 'gait_cycle'}
    # The native Sprint impulse player loops in the movement sync group.
    # Its historical family label must not turn it into an idle-to-run clip.
    sprint_impulse = by_name.get('Sprint_Acceleration')
    if sprint_impulse and any(b.get('sampling', {}).get('bLoopAnimation') is True
                             for b in sprint_impulse['sourceBindings']):
        allowed_gaits.add('Sprint_Acceleration')
    if set(gait_closure) - allowed_gaits:
        raise ValueError('Gait closure requires a native looping locomotion source')
    overrides = rules.get('locomotionOverrides', {})
    if set(overrides) - allowed_gaits:
        raise ValueError('Locomotion overrides require native looping locomotion sources')
    programmatic=rules.get('programmaticGaits',{})
    for name,source in programmatic.items():
        if name not in allowed_gaits or source not in allowed_gaits or name!=source.replace('Forward','Backward'):
            raise ValueError('A reversed mirror requires corresponding native forward/backward gaits')
        if by_name[name]['originalTiming']!=by_name[source]['originalTiming']:
            raise ValueError('A reversed mirror requires identical native time domains')

    def add(key, op, deps=(), **params):
        if key in nodes:
            raise ValueError(f'Duplicate node {key}')
        nodes[key] = {'id': key, 'op': op, 'deps': list(dict.fromkeys(deps)), 'params': params}
        return key

    def motion(name):
        if name not in by_name:
            raise ValueError(f'Unknown semantic motion: {name}')
        return f'motion:{name}'

    def reference(name, frame=0):
        key = f'reference:{name}:{frame}'
        if key not in nodes:
            row = by_name[name]
            if not 0 <= frame < row['originalTiming']['sampledKeyCount']:
                raise ValueError(f'Invalid base frame: {name}[{frame}]')
            if row['family'] in ('pose_table','pose_table_copy'):
                table=name.removesuffix('_NoCurves')
                slot=f'pose:{table}:{frame}'
                add(key, 'freeze', [slot], source=slot, frame=0, count=1, tableFrame=frame)
            else:
                add(key, 'freeze', [motion(name)], source=motion(name), frame=frame, count=1)
        return key

    def overlay_base(name):
        if name != 'Walk_Forward_Pose':
            return motion(name)
        key = 'reference:walking-overlay-base'
        if key not in nodes:
            stand, walk = motion('Stand_Pose'), motion(name)
            add(key, 'upper_overlay', ['rig:soma', stand, walk], base=stand, style=walk, reference=stand)
        return key

    def generate(key, row, deps, anchors=(), prompt=None, duration=None, path=None):
        params = dict(prompt=prompt or row['promptDraft'], seconds=duration or rules['candidateSeconds'],
                      fps=rules['fps'], seed=rules['seed'], model=rules['model'],
                      steps=rules['diffusionSteps'], anchors=list(anchors), rootPath=path,
                      semantic=row['semanticName'])
        if row['family']=='idle_additive':
            params['endEffectors']=[{'type':kind,'source':deps[0],'at':[0,.5,1]} for kind in ('left-foot','right-foot')]
        elif row['family'] in ('weapon_cycle','weapon_start') or 'Crouch_Rifle' in row['semanticName']:
            params['endEffectors']=[{'type':kind,'source':deps[0],'at':[0,.25,.5,.75,1]} for kind in ('left-hand','right-hand')]
        if not params['prompt'] or '{' in params['prompt']:
            raise ValueError(f'Unmaterialized prompt in {key}')
        return add(key, 'generate', ['rig:soma', *deps], **params)

    add('rig:soma', 'rig')
    # Every target is a candidate; publication is deliberately outside this DAG.
    for name, row in sorted(by_name.items()):
        key = motion(name)
        if key in nodes:
            continue
        family = row['family']
        stance = 'Crouch_Pose' if 'Crouch' in name else 'Stand_Pose'
        base = motion(stance)
        ev = row['originalEvaluation']
        exact_base = None
        if ev['baseAsset']:
            exact_base = reference(by_source[ev['baseAsset']]['semanticName'], ev['baseFrame'])
        # Right variants are derived, never separate random samples.
        left = name.replace('Right', 'Left')
        mirrored = left != name and left in by_name and family in {
            'gait_cycle', 'jump', 'turn', 'rotate', 'foot_transition', 'gait_pose'}
        if family=='gait_pose':
            parent_name=name.removesuffix('_Pose')
            if parent_name not in by_name:parent_name+='_Forward'
            if parent_name.replace('Right','Left') in gait_closure:mirrored=False
        if mirrored:
            add(key, 'mirror', ['rig:soma', motion(left), exact_base or base],
                source=motion(left), base=exact_base or base,
                mask='legs' if 'Rifle' in name else 'all',
                repairDerived=bool(gait_closure.get(left,{}).get('repairMirroredCycle',False)))
        elif family == 'mantle':
            from .mantle import profile, INDEX, POLICY
            spec = profile(name)
            native = 'reference:' + name + ':native-mantle'
            add(native, 'native_mantle_reference', ['rig:soma'], indexPath=INDEX, semantic=name)
            candidate = 'candidate:' + name
            # No dependency on the generated Stand pose, no fixed-height end
            # stand pin and no gait mirror/loop operation for contact actions.
            hands = 'both hands' if len(spec['hands']) == 2 else spec['hands'][0] + ' hand'
            prompt = ('A person mantles onto a ' + ('high' if 'High' in name else 'low') +
                ' ledge, planting ' + hands + ' on the top surface, palms down and fingers forward. '
                'Keep the supporting palms fixed while pulling and pushing the body up, '
                'then release the hands and finish standing with both feet on the ledge.')
            generate(candidate, row, [native], [], prompt=prompt,
                     duration=max(2., row['originalTiming']['sampledKeyCount']/rules['fps']), path=None)
            nodes[candidate]['params']['mantleReference'] = native
            add(key, 'mantle', ['rig:soma', candidate, native], source=candidate,
                reference=native, semantic=name, policy=POLICY)
        elif name in programmatic:
            add(key,'mirror',['rig:soma',motion(programmatic[name]),base],
                source=motion(programmatic[name]),base=base,mask='all',reverseTime=True,
                repairDerived=bool(gait_closure.get(name,{}).get('repairMirroredCycle',False)))
        elif family == 'base_pose' and name == 'Lean_Pose':
            add(key, 'freeze', [motion('Run_Forward')], source=motion('Run_Forward'), fraction=0.25, count=1)
        elif family == 'base_pose':
            candidate = f'candidate:{name}'
            deps = [] if name == 'Stand_Pose' else [motion('Stand_Pose')]
            anchors = [] if not deps else [{'source': deps[0], 'sourceFrame': 0, 'at': 0}]
            prompt = row['promptDraft'] if not deps else 'A person lowers from standing into a stable crouch with both feet planted.'
            generate(candidate, row, deps, anchors, prompt=prompt)
            add(key, 'freeze', ['rig:soma', candidate], source=candidate, fraction=rules['poseSampleFraction'], count=1, canonical=True)
        elif family == 'reference':
            add(key, 'rest', ['rig:soma'])
        elif family == 'prop_animation':
            add(key, 'external', payloadType='prop', reason='Bow rig is not a SOMA humanoid; bind a JSON with rigPath/motionPath for authored prop motion.')
        elif family == 'pose_table_copy':
            parent = motion(name.removesuffix('_NoCurves'))
            add(key, 'copy', [parent], source=parent, removeCurves=True)
        elif family == 'pose_table':
            count = row['originalTiming']['sampledKeyCount']
            layout = rules.get('poseLayouts', {}).get(name, {})
            parts = []
            for frame in range(count):
                slot = f'pose:{name}:{frame}'
                spec = layout.get(str(frame))
                if isinstance(spec, str):
                    source = overlay_base(spec)
                    add(slot, 'freeze', [source], source=source, frame=0, count=1)
                elif isinstance(spec, dict):
                    if 'copyFrame' in spec:
                        previous=spec['copyFrame']
                        if not isinstance(previous,int) or not 0<=previous<frame:
                            raise ValueError(f'Pose endpoint must copy an earlier frame: {slot}')
                        previous_slot=f'pose:{name}:{previous}'
                        add(slot,'copy',[previous_slot],source=previous_slot,endpointFrame=previous)
                        parts.append(slot)
                        continue
                    slot_base = overlay_base(spec['base'])
                    if 'upperFromFrame' in spec:
                        previous=spec['upperFromFrame']
                        if not isinstance(previous,int) or not 0<=previous<frame:
                            raise ValueError(f'Upper overlay must reference an earlier pose: {slot}')
                        style=f'pose:{name}:{previous}';ref=motion(spec['reference'])
                        add(slot,'upper_overlay',['rig:soma',slot_base,style,ref],base=slot_base,style=style,reference=ref)
                        parts.append(slot)
                        continue
                    candidate = f'candidate:{name}:{frame}'
                    generate(candidate, row, [slot_base],
                             [{'source': slot_base, 'sourceFrame': spec.get('baseFrame', 0), 'at': 0}],
                             prompt=spec['prompt'])
                    if spec.get('preserveLowerBody'):
                        frozen=f'style:{name}:{frame}'
                        add(frozen,'freeze',[candidate],source=candidate,fraction=rules['poseSampleFraction'],count=1)
                        add(slot,'upper_overlay',['rig:soma',slot_base,frozen],base=slot_base,style=frozen,reference=slot_base)
                    else:
                        add(slot, 'freeze', [candidate], source=candidate, fraction=rules['poseSampleFraction'], count=1)
                else:
                    add(slot, 'external', ['rig:soma'], reason=f'Author exact meaning/pose of {name}[{frame}] in poseLayouts or bind approved SOMA pose.',
                        sourceEvidence=row['sourceBindings'], additiveDependents=row.get('additiveDependents', []))
                parts.append(slot)
            add(key, 'pack', ['rig:soma', *parts], parts=parts, fps=rules['fps'])
        elif family == 'gait_pose':
            parent_name = name.removesuffix('_Pose')
            if parent_name == 'Crouch_Walk': parent_name += '_Forward'
            if parent_name not in by_name: parent_name += '_Forward'
            add(key, 'freeze', [motion(parent_name)], source=motion(parent_name), fraction=0.25,
                count=row['originalTiming']['sampledKeyCount'])
        elif family in ('lean_additive', 'acceleration_additive'):
            if not exact_base: raise ValueError(f'No additive base for {name}')
            angle = rules['leanDegrees'] if family == 'lean_additive' else rules['accelerationDegrees']
            direction = name.rsplit('_', 1)[-1]
            axis = 'z' if direction in ('Left', 'Right') else 'x'
            angle *= -1 if direction in ('Left', 'Backward') else 1
            if direction not in ('Left', 'Right', 'Forward', 'Backward'): angle = 0
            add(key, 'offset', ['rig:soma', exact_base], base=exact_base, axis=axis,
                angles={'Spine1': angle * .25, 'Spine2': angle * .25, 'Chest': angle * .5},
                count=row['originalTiming']['sampledKeyCount'])
        elif family in ('look_grid', 'prop_aim'):
            if not exact_base: raise ValueError(f'No parameter base for {name}')
            if family == 'look_grid' and name != 'Look_Forward':
                parent = motion('Look_Forward')
                add(key, 'parameter', ['rig:soma', parent, exact_base], base=exact_base, source=parent,
                    mode='pitch_row', pitch=rules['lookPitchPhysicalLimitDegrees'] * (1 if 'Down' in name else -1),
                    distribution=rules['aimPitchDistribution'], count=31)
            else:
                add(key, 'parameter', ['rig:soma', exact_base], base=exact_base,
                    mode='yaw' if family == 'look_grid' else 'pitch',
                    limit=rules['lookYawPhysicalLimitDegrees'] if family == 'look_grid' else rules['lookPitchPhysicalLimitDegrees'],
                    distribution=rules['lookYawDistribution'] if family == 'look_grid' else rules['aimPitchDistribution'], count=31)
        elif family == 'land_additive':
            parent = motion(name.removesuffix('_Additive'))
            add(key, 'copy', [parent, exact_base], source=parent, fullPoseForTargetRigAdditive=True)
        elif family in ('weapon_cycle', 'weapon_start') and 'Arms' in name:
            parent = motion(name.replace('_Arms', ''))
            add(key, 'copy', [parent], source=parent, targetRigTrackPolicy='same_master_then_apply_authored_layer_mask')
        elif name == 'CrouchToStand':
            add(key, 'reverse', [motion('StandToCrouch')], source=motion('StandToCrouch'))
        else:
            deps = [exact_base or base]
            # Actual constraint poses are extracted from produced dependencies at run time.
            anchors = [{'source': deps[0], 'sourceFrame': 0, 'at': 0}]
            path = {'kind': 'fixed', 'headingDegrees': 0}
            if family in ('gait_cycle', 'weapon_cycle', 'weapon_start', 'sprint_start'):
                if family in ('weapon_cycle', 'weapon_start') or 'Rifle' in name:
                    grip = reference('Rifle_Poses_NoCurves', 9 if 'Crouch' in name else 1)
                    deps = [grip]; anchors[0]['source'] = grip
                velocity = 6 if 'Sprint' in name else 3.5 if 'Run' in name else 1.5
                from tools.unreal.build_als_kimodo_asset_contracts import direction
                path = {'kind': 'accelerate' if family in ('weapon_start','sprint_start') else 'linear',
                        'velocity': velocity, 'direction': direction(name)[1], 'headingDegrees': 0}
            override = overrides.get(name, {})
            if override:
                if set(override) - {'prompt', 'direction', 'headingDegrees', 'velocity', 'reference'}:
                    raise ValueError('Unknown locomotion override: ' + name)
                path = {**path, 'kind': 'linear',
                        **{k: override[k] for k in ('direction', 'headingDegrees', 'velocity') if k in override}}
                anchors = []
                if override.get('reference'):
                    ref_motion = motion(override['reference'])
                    deps.append(ref_motion)
                    anchors = [{'source': ref_motion, 'fraction': .25, 'at': 0}]
            if family == 'idle_additive':
                anchors.append({'source': deps[0], 'sourceFrame': 0, 'at': 1})
            if family == 'stance_transition':
                deps.append(motion('Crouch_Pose'))
                anchors.append({'source': motion('Crouch_Pose'), 'sourceFrame': 0, 'at': 1})
            if family == 'jump' and name != 'Jump_Loop':
                gait = motion('Run_Forward' if 'Run' in name else 'Walk_Forward')
                deps.append(gait); anchors[0] = {'source': gait, 'fraction': .25, 'at': 0}
                deps.append(motion('Jump_Loop'))
                anchors.append({'source': motion('Jump_Loop'), 'sourceFrame': 0, 'at': 1})
            if family in ('turn', 'rotate'):
                path['headingDegrees'] = (180 if '180' in name else 90) if 'Left' in name else -90
                path['kind'] = 'turn'
            if family in ('getup', 'land', 'roll', 'mantle', 'foot_transition'):
                # Their initial pose belongs to another state; don't pin it to standing.
                anchors = [{'source': deps[0], 'sourceFrame': 0, 'at': 1}]
            if family=='roll':
                # Gameplay roll must travel; a fixed root path creates an in-place
                # tumble with almost zero net actor displacement after extraction.
                path={'kind':'linear','velocity':rules.get('rollTravelMeters',1.8)/(rules['candidateSeconds']-1/rules['fps']),
                      'direction':[0,1],'headingDegrees':0}
            if family in ('fall', 'flail'):
                anchors = []
            candidate = f'candidate:{name}'
            if name in gait_closure:
                spec = gait_closure[name]
                duration = spec.get('candidateSeconds', 5)
                if spec.get('candidateCycles'):
                    duration=max(2.,(spec['candidateCycles']*(row['originalTiming']['sampledKeyCount']-1)+1)/rules['fps'])
                if name not in allowed_gaits or not 2 <= duration <= 10:
                    raise ValueError('Invalid gait closure candidate definition')
                discovery = candidate + ':discovery'
                generate(discovery, row, deps, anchors, prompt=override.get('prompt'), path=path,
                    duration=spec.get('discoverySeconds',duration))
                reference_node = discovery
                if spec.get('constraintReference')=='native_als':
                    reference_node='reference:'+name+':native-als'
                    add(reference_node,'native_gait_reference',['rig:soma'],
                        indexPath='assets/config/refactored_animation_sources.json',source=row['source'],
                        expectedSpeedMps=path['velocity'],direction=path['direction'])
                elif spec.get('repairReference'):
                    reference_node = 'reference:' + name + ':repaired'
                    add(reference_node, 'periodic_repair', ['rig:soma', discovery], source=discovery,
                        expectedSpeedMps=path['velocity'], harmonics=spec.get('repairHarmonics', 4),
                        targetPeriodFrames=row['originalTiming']['sampledKeyCount']-1 if override else None)
                generate(candidate, row, [*deps, reference_node], [], prompt=override.get('prompt'), path=path, duration=duration)
                nodes[candidate]['params'].update(gaitReference=reference_node,
                    targetPeriodSeconds=row['originalTiming']['playLength'],
                    gaitPolicy=spec.get('policy', {}),
                    constrainClipEndpoint=bool(override),
                    boundaryContextFrames=spec.get('boundaryContextFrames', 0))
                if override:
                    nodes[candidate]['params'].update(matchStrideToPeriod=True,
                        fullBodyPhaseFractions=[.25, .5, .75])
                if spec.get('maximumConstraintCycles'):
                    nodes[candidate]['params']['maximumConstraintCycles'] = spec['maximumConstraintCycles']
                for option in ('fullBodyPhaseCycles','supportAnchorCount','supportPairSpacingSeconds',
                               'supportWindowFraction','maximumAnchorCorrectionMeters','constrainHandOrientations'):
                    if option in spec:nodes[candidate]['params'][option]=spec[option]
                selected_source = candidate
                if spec.get('repairAfterGeneration'):
                    selected_source='repair:'+name+':generated'
                    add(selected_source,'periodic_repair',['rig:soma',candidate,reference_node],
                        source=candidate,contactReference=reference_node,constrainedCycleIndex=spec.get('repairCycleIndex',0),
                        expectedSpeedMps=path['velocity'],targetPeriodFrames=row['originalTiming']['sampledKeyCount']-1,
                        harmonics=spec.get('finalRepairHarmonics',3),rampFrames=spec.get('repairRampFrames',4),
                        minimumKneeBendDegrees=spec.get('minimumKneeBendDegrees',15),
                        fitRegularization=spec.get('fitRegularization',.02),enforceSoleClearance=True)
                if spec.get('selectBestReference'):
                    if not spec.get('repairReference'):
                        raise ValueError('Best-reference selection requires a repaired reference')
                    selected_source = 'selection:' + name
                    add(selected_source, 'gait_selection', ['rig:soma', reference_node, candidate],
                        sources=[reference_node, candidate], expectedSpeedMps=path['velocity'])
                add(key, 'gait_cycle', ['rig:soma', selected_source], source=selected_source,
                    targetCount=row['originalTiming']['sampledKeyCount'],
                    expectedSpeedMps=path['velocity'], policy=spec.get('policy', {}))
                if spec.get('upperBodyIdleReference'):
                    idle = motion(spec['upperBodyIdleReference'])
                    if not name.startswith('Walk_'):
                        raise ValueError('Idle-relative relaxed arm limits apply to unarmed Walk only')
                    nodes[key]['deps'].append(idle)
                    nodes[key]['params'].update(upperBodyIdleReference=idle,
                        upperBodyPolicy=spec.get('upperBodyPolicy', {}))
            else:
                generate(candidate, row, deps, anchors, prompt=override.get('prompt'), path=path)
            if name in gait_closure:
                pass  # The event-driven authoring operation owns its quality receipt.
            elif row['constraintRecipe']['required_loop']:
                add(key, 'loop', ['rig:soma', candidate], source=candidate,
                    seconds=max(row['originalTiming']['playLength'], .3), startFraction=.35,
                    removeHeading=family == 'rotate', removeTranslation=family in ('gait_cycle','weapon_cycle'))
            elif family in ('sprint_start','weapon_start'):
                add(key,'in_place',['rig:soma',candidate],source=candidate,rootPath=path,seconds=rules['candidateSeconds'])
            else:
                add(key, 'copy', [candidate], source=candidate)

    for row in rows:
        name = row['semanticName']; source = motion(name); ev = row['originalEvaluation']
        if row['family'] == 'mantle':
            from .mantle import POLICY
            row = dict(row, targetMantlePolicy=POLICY)
        closure_name = name if name in gait_closure else name.replace('Right', 'Left')
        if closure_name in gait_closure and row['family'] != 'gait_pose':
            row = dict(row, targetGaitPolicy='als-forward-v1')
            if gait_closure[closure_name].get('upperBodyIdleReference'):
                row['targetUpperBodyPolicy'] = 'walk-idle-relative-upper-body-v1'
            if gait_closure[closure_name].get('targetCleanup'):
                if name != 'Walk_Forward':
                    raise ValueError('Target stance cleanup currently supports forward Walk only')
                row['targetCleanup'] = {'phaseShiftFrames': 1}
        if row['family']=='prop_animation':
            add(f"asset:{row['edition']}:{row['asset']}", 'deliver_prop', [source],
                source=source,contract=row,requires=['prop_rig_validation','humanoid_grip_sync','final_skeleton_acceptance'])
            continue
        base = reference(by_source[ev['baseAsset']]['semanticName'], ev['baseFrame']) if ev['baseAsset'] else None
        origin = target_origin_reference(row, rows)
        origin_motion = motion(by_source[origin['source']]['semanticName']) if origin else None
        add(f"asset:{row['edition']}:{row['asset']}", 'deliver', ['rig:soma', source, *([base] if base else []),
            *([origin_motion] if origin_motion else [])],
            source=source, base=base, targetPlanarOrigin=origin, contract=row,
            requires=['target_rig_retarget', 'new_contact_sync_curve_notify_authoring', 'final_skeleton_acceptance'])
    order = topological_order(nodes)
    return {'schemaVersion': 1, 'status': 'authoring_dag_not_production_publish',
            'contractsDigest': digest(contracts), 'rulesDigest': digest(rules), 'rules': rules,
            'nodes': nodes, 'order': order,
            'summary': {'assets': len(rows), 'semanticMotions': len(by_name), 'nodes': len(nodes),
                        'operations': dict(sorted(Counter(n['op'] for n in nodes.values()).items()))}}


def mermaid(graph):
    lines = ['flowchart LR']
    ids = {key: f'n{i}' for i, key in enumerate(graph['order'])}
    for key in graph['order']:
        node = graph['nodes'][key]
        label = re.sub(r'[^A-Za-z0-9_ :.-]', '_', key)
        lines.append(f'  {ids[key]}["{node["op"]}: {label}"]')
        for dep in node['deps']: lines.append(f'  {ids[dep]} --> {ids[key]}')
    return '\n'.join(lines) + '\n'

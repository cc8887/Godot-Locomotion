"""Write physical ALS tracks directly, then independently evaluate them with ufbx.

No engine, FBX SDK, resampling, virtual bones, or implicit root correction.
The FBX bind pose remains the immutable target skeleton's reference pose.
"""
from pathlib import Path
import copy
import warnings
import numpy as np
from scipy.spatial.transform import Rotation

TICKS = 46186158000
FBX_BASIS = np.diag([1.,-1.,1.])  # UE target native -> original exported FBX.


def _fbx_quaternions(values):
    return Rotation.from_matrix(FBX_BASIS@Rotation.from_quat(values).as_matrix()@FBX_BASIS).as_quat()


def mesh_binding_skeleton(skeleton, mesh_path):
    """Use the immutable target MESH's exact case-sensitive exported names."""
    import ufbx
    scene=ufbx.load_file(str(mesh_path)); node_list=scene.nodes
    expected={n.lower() for n in skeleton['rawBoneNames']}
    names={}
    for node in node_list:
        if node.name.lower() not in expected:continue
        if node.name.lower() in names:raise ValueError('Ambiguous mesh bone name')
        names[node.name.lower()]=node.name
    if set(names)!=expected:raise ValueError('Target mesh physical names do not cover the ALS skeleton')
    result=copy.deepcopy(skeleton)
    result['rawBoneNames']=[names[n.lower()] for n in skeleton['rawBoneNames']]
    return result


def _numbers(values):
    return ','.join(format(float(x), '.17g') for x in np.asarray(values).reshape(-1))


def write_fbx(path, raw, skeleton):
    names = skeleton['rawBoneNames']; parents = skeleton['rawParents']
    count = raw['sampledKeyCount']; fps = raw['frameRateNumerator']/raw['frameRateDenominator']
    times = np.arange(count)/fps
    if count < 1 or abs(times[-1]-raw['playLength']) > 1e-5:
        raise ValueError('FBX timeline must match the exported raw keys')
    tracks = {t['bone'].lower(): t for t in raw['tracks']}
    if len(tracks) != len(names) or set(tracks) != {n.lower() for n in names}:
        raise ValueError('FBX needs exactly all physical tracks')
    lines = ['; FBX 7.4.0 project file', 'FBXHeaderExtension: {', ' FBXHeaderVersion: 1003',
             ' FBXVersion: 7400', ' Creator: "Kimodo ALS offline physical-track writer"', '}',
             'GlobalSettings: {', ' Version: 1000', ' Properties70: {',
             '  P: "UpAxis", "int", "Integer", "",2', '  P: "UpAxisSign", "int", "Integer", "",1',
             '  P: "FrontAxis", "int", "Integer", "",0', '  P: "FrontAxisSign", "int", "Integer", "",1',
             '  P: "CoordAxis", "int", "Integer", "",1', '  P: "CoordAxisSign", "int", "Integer", "",1',
             '  P: "UnitScaleFactor", "double", "Number", "",1',
             '  P: "TimeMode", "enum", "", "",14', f'  P: "CustomFrameRate", "double", "Number", "",{fps:.17g}',
             ' }', '}', 'Documents: {', ' Count: 1', ' Document: 1, "Scene", "Scene" {',
             '  RootNode: 0', ' }', '}', 'Definitions: {', ' Version: 100',
             f' Count: {2+13*len(names)}', '}', 'Objects: {',
             ' AnimationStack: 10, "AnimStack::Unreal Take", "" {', '  Properties70: {',
             '   P: "LocalStart", "KTime", "Time", "",0',
             f'   P: "LocalStop", "KTime", "Time", "",{round(times[-1]*TICKS)}',
             '   P: "ReferenceStart", "KTime", "Time", "",0',
             f'   P: "ReferenceStop", "KTime", "Time", "",{round(times[-1]*TICKS)}',
             '  }', ' }', ' AnimationLayer: 11, "AnimLayer::BaseLayer", "" { }']
    connections = [' C: "OO",11,10']
    for i, name in enumerate(names):
        model = 1000+i; attr = 2000+i
        ref = skeleton['referencePose'][i]
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            ref_euler = Rotation.from_quat(_fbx_quaternions(ref['rotation'])).as_euler('xyz', degrees=True)
        lines += [f' Model: {model}, "Model::{name}", "LimbNode" {{', '  Version: 232',
                  '  Properties70: {', '   P: "RotationOrder", "enum", "", "",0',
                  '   P: "RotationActive", "bool", "", "",1']
        for prop, value in [('Lcl Translation', FBX_BASIS@ref['position']), ('Lcl Rotation', ref_euler), ('Lcl Scaling', ref['scale'])]:
            lines.append(f'   P: "{prop}", "{prop}", "", "A",{_numbers(value)}')
        lines += ['  }', '  Shading: T', '  Culling: "CullingOff"', ' }',
                  f' NodeAttribute: {attr}, "NodeAttribute::{name}", "LimbNode" {{',
                  '  TypeFlags: "Skeleton"', ' }']
        connections += [f' C: "OO",{model},{1000+parents[i] if parents[i]>=0 else 0}', f' C: "OO",{attr},{model}']
        t = tracks[name.lower()]
        for channel, field, prop in [(0, 'positions', 'Lcl Translation'), (1, 'rotations', 'Lcl Rotation'), (2, 'scales', 'Lcl Scaling')]:
            width = 4 if channel == 1 else 3
            values = np.broadcast_to(np.asarray(t[field], dtype=float), (count, width))
            if not np.isfinite(values).all(): raise ValueError('Non-finite FBX channel')
            if channel == 0:values=values@FBX_BASIS
            if channel == 1:
                with warnings.catch_warnings():
                    warnings.simplefilter('ignore', UserWarning)
                    values = np.unwrap(Rotation.from_quat(_fbx_quaternions(values)).as_euler('xyz'), axis=0)*180/np.pi
            node_id = 10000+i*20+channel*5
            lines += [f' AnimationCurveNode: {node_id}, "AnimCurveNode::{name}_{channel}", "" {{', '  Properties70: {']
            for axis in range(3): lines.append(f'   P: "d|{"XYZ"[axis]}", "Number", "", "A",{values[0,axis]:.17g}')
            lines += ['  }', ' }']
            connections += [f' C: "OO",{node_id},11', f' C: "OP",{node_id},{model},"{prop}"']
            for axis in range(3):
                curve_id = node_id+axis+1
                lines += [f' AnimationCurve: {curve_id}, "AnimCurve::{name}_{channel}_{axis}", "" {{',
                          f'  Default: {values[0,axis]:.17g}', '  KeyVer: 4008', f'  KeyTime: *{count} {{',
                          '   a: '+','.join(str(round(t*TICKS)) for t in times), '  }', f'  KeyValueFloat: *{count} {{',
                          '   a: '+_numbers(values[:,axis]), '  }', '  KeyAttrFlags: *1 {', '   a: 24836', '  }',
                          '  KeyAttrDataFloat: *4 {', '   a: 0,0,0,0', '  }', '  KeyAttrRefCount: *1 {', f'   a: {count}', '  }', ' }']
                connections.append(f' C: "OP",{curve_id},{node_id},"d|{"XYZ"[axis]}"')
    # ALS's existing imported-animation adapter binds this original take name.
    lines += ['}', 'Connections: {']+connections+['}', 'Takes: {', ' Current: "Unreal Take"', '}']
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text('\n'.join(lines)+'\n', encoding='utf-8')
    return path


def readback(path, raw, skeleton):
    """Native ufbx local transforms at EVERY authored key, before axis conversion."""
    import ufbx
    scene = ufbx.load_file(str(path))
    # ufbx 0.0.5 children borrow their list owner. Keep these wrappers alive;
    # extracting nodes from a temporary scene.nodes list can access freed data.
    node_list = scene.nodes; stack_list = scene.anim_stacks
    nodes = {n.name: n for n in node_list if n.name}
    names = skeleton['rawBoneNames']; tracks = {t['bone'].lower(): t for t in raw['tracks']}
    if set(nodes) != set(names): raise ValueError('FBX physical bone inventory differs')
    if len(stack_list) != 1: raise ValueError('FBX animation stack missing/ambiguous')
    stack = stack_list[0]; anim = stack.anim; count = raw['sampledKeyCount']
    errors = dict(positionCm=0., rotationDegrees=0., scale=0., bindPositionCm=0., bindRotationDegrees=0.)
    for i, name in enumerate(names):
        node = nodes[name]; parent = skeleton['rawParents'][i]
        if parent >= 0 and node.parent.name != names[parent]: raise ValueError('FBX parent differs: '+name)
        ref = skeleton['referencePose'][i]; rest = node.local_transform
        errors['bindPositionCm'] = max(errors['bindPositionCm'], float(np.max(np.abs(FBX_BASIS@rest.translation-ref['position']))))
        errors['bindRotationDegrees'] = max(errors['bindRotationDegrees'], float((Rotation.from_quat(_fbx_quaternions(rest.rotation)).inv()*Rotation.from_quat(ref['rotation'])).magnitude()*180/np.pi))
        track = tracks[name.lower()]
        p = np.broadcast_to(track['positions'], (count,3)); q = np.broadcast_to(track['rotations'], (count,4)); s = np.broadcast_to(track['scales'], (count,3))
        for frame in range(count):
            t = frame*raw['frameRateDenominator']/raw['frameRateNumerator']
            got = node.evaluate_transform(anim,t)
            errors['positionCm'] = max(errors['positionCm'], float(np.max(np.abs(FBX_BASIS@got.translation-p[frame]))))
            errors['rotationDegrees'] = max(errors['rotationDegrees'], float((Rotation.from_quat(_fbx_quaternions(got.rotation)).inv()*Rotation.from_quat(q[frame])).magnitude()*180/np.pi))
            errors['scale'] = max(errors['scale'], float(np.max(np.abs(np.asarray(got.scale)-s[frame]))))
    passed = errors['positionCm'] <= .01 and errors['rotationDegrees'] <= .1 and errors['scale'] <= 1e-4 and errors['bindPositionCm'] <= .01 and errors['bindRotationDegrees'] <= .1
    return dict(policy='ufbx-all-physical-keys-v1', passed=passed, physicalBones=len(names), sampledKeyCount=count,
                errors=errors, reader='ufbx 0.0.5', axisConversion='UE native <-> original exported FBX Y reflection')

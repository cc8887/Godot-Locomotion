// Native SOMA data is already in physical local axes; no BVH pose-basis rewrite.
export function sourceMotionObject(data, THREE) {
  if (data.format !== 'kimodo-viewer-motion-v1' || data.units !== 'cm' || !['y','z'].includes(data.up)) {
    throw new Error('不支持的源动画格式或坐标约定');
  }
  const count = data.frameCount, names = data.jointNames;
  if (!(data.fps > 0) || count < 1 || data.localQuaternions.length !== count ||
      (data.localPositionsCm || data.rootPositionsCm).length !== count || data.parents.length !== names.length ||
      data.offsetsCm.length !== names.length) throw new Error('源动画维度不匹配');
  const wrapper = new THREE.Bone();
  wrapper.name = 'Root'; // Static display origin, never an authored motion track.
  wrapper.userData.displayRootOnly = !!data.displayRootOnly;
  const bones = names.map((name, i) => {
    const bone = new THREE.Bone(); bone.name = name;
    bone.position.fromArray(data.localPositionsCm ? data.localPositionsCm[0][i] : i === 0 ? data.rootPositionsCm[0] : data.offsetsCm[i]);
    bone.quaternion.fromArray(data.localQuaternions[0][i]);
    return bone;
  });
  bones.forEach((bone, i) => {
    const parent = data.parents[i];
    if (i === 0 && parent === -1) wrapper.add(bone);
    else if (parent >= 0 && parent < i) bones[parent].add(bone);
    else throw new Error('源骨架层级顺序错误');
  });
  const times = Array.from({length:count}, (_, i) => i/data.fps);
  const tracks = data.localPositionsCm ? [] : [new THREE.VectorKeyframeTrack(names[0]+'.position', times, data.rootPositionsCm.flat())];
  for (let bone = 0; bone < names.length; bone++) {
    tracks.push(new THREE.QuaternionKeyframeTrack(names[bone]+'.quaternion', times,
      data.localQuaternions.flatMap(frame => frame[bone])));
    if (data.localPositionsCm) tracks.push(new THREE.VectorKeyframeTrack(names[bone]+'.position',times,data.localPositionsCm.flatMap(frame=>frame[bone])));
    if (data.localScales) tracks.push(new THREE.VectorKeyframeTrack(names[bone]+'.scale',times,data.localScales.flatMap(frame=>frame[bone])));
  }
  return {object:data.displayRootOnly ? wrapper : bones[0], clip:new THREE.AnimationClip(data.name, (count-1)/data.fps, tracks)};
}

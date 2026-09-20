import fs from 'node:fs';
import assert from 'node:assert/strict';

const [requestPath, capturePath, output] = process.argv.slice(2);
assert(requestPath && capturePath && output, 'Expected authored request, UE capture, fresh report path.');
const request=JSON.parse(fs.readFileSync(requestPath,'utf8'));
const data=JSON.parse(fs.readFileSync(capturePath,'utf8'));
assert.equal(data.schemaVersion,1); assert.equal(data.samples.length,24);
assert.equal(request.samples.length,24);
const key = s=>s.version+':'+s.side+':'+s.node;
const samples=new Map();
for(const row of data.samples) {
  assert(!samples.has(key(row.input)));
  assert.deepEqual(row.input,request.samples.find(s=>key(s)===key(row.input)));
  assert(row.frameRate.numerator>0 && row.frameRate.denominator>0);
  const seconds=row.input.mode==='frame' ? row.input.sample*row.frameRate.denominator/row.frameRate.numerator : row.input.sample;
  assert.equal(row.seconds,seconds);
  samples.set(key(row.input),row);
}
const qangle=(a,b)=>2*Math.acos(Math.min(1,Math.abs(a.reduce((s,v,i)=>s+v*b[i],0))/(Math.hypot(...a)*Math.hypot(...b))))*180/Math.PI;
const pos=(a,b)=>Math.hypot(...a.map((v,i)=>v-b[i]));
const pairs=[];
const directions=new Map([
  ['F','Forward'],['B','Backward'],['LF','Left_Forward'],
  ['LB','Left_Backward'],['RF','Right_Forward'],['RB','Right_Backward'],
]);
for(const sample of request.samples.filter(s=>s.version==='Refactored')) {
  const ref=samples.get(key(sample)),v4=samples.get(key({...sample,version:'V4'}));
  assert(v4);
  const oldDirection=v4.input.source.split('/').at(-1).match(/^ALS_N_Walk_(F|B|LF|LB|RF|RB)\.ALS_N_Walk_\1$/)?.[1];
  const nativeDirection=directions.get(oldDirection);
  assert(nativeDirection && sample.source.endsWith('/A_Als_Walk_'+nativeDirection+'.A_Als_Walk_'+nativeDirection),
    'A reused editor node name cannot establish matching source animation semantics.');
  const oldBones=new Map(v4.bones.map(b=>[b.name.toLowerCase(),b]));
  const nativeBones=new Map(ref.bones.map(b=>[b.name.toLowerCase(),b]));
  const bones=[];
  for(const name of ['pelvis','thigh_l','thigh_r','foot_l','foot_r','ik_foot_l','ik_foot_r']) {
    const a=oldBones.get(name),b=nativeBones.get(name); assert(a&&b);
    bones.push({name,positionCm:pos(a.component.position,b.component.position),
      rotationDegrees:qangle(a.component.rotation,b.component.rotation),v4:a.component,refactored:b.component});
  }
  const curves=[];
  for(const [v4Name,refName] of [['FootLock_L','FootLeftLock'],['FootLock_R','FootRightLock'],['Feet_Position','FootPlanted'],['Feet_Crossing','FeetCrossing']])
    curves.push({v4Name,refName,v4:v4.curves[v4Name]??null,refactored:ref.curves[refName]??null});
  pairs.push({side:sample.side,direction:nativeDirection,node:sample.node,v4Source:v4.input.source,refactoredSource:ref.input.source,
    v4Time:v4.seconds,refactoredTime:ref.seconds,bones,curves});
}
const report={scope:'Two native authored Plant source versions; numeric differences are observations, not a parity pass or whole-character explanation',
  pairs,maximumSelectedPositionCm:Math.max(...pairs.flatMap(p=>p.bones.map(b=>b.positionCm))),
  maximumSelectedRotationDegrees:Math.max(...pairs.flatMap(p=>p.bones.map(b=>b.rotationDegrees)))};
fs.writeFileSync(output,JSON.stringify(report,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({pairs:pairs.length,maximumSelectedPositionCm:report.maximumSelectedPositionCm,
  maximumSelectedRotationDegrees:report.maximumSelectedRotationDegrees,
  leftFootTargets:pairs.filter(p=>p.side==='Left').map(p=>({node:p.node,v4Time:p.v4Time,refactoredTime:p.refactoredTime,
    ik:p.bones.find(b=>b.name==='ik_foot_l'),curves:p.curves}))},null,2));

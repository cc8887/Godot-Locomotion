// Compare the six local arm-chain bones after final evaluation. The V4 graph
// runs hands before feet; production Refactored feet run before hands. This is
// explicitly not a comparison of their pelvis, feet or world-space contacts.
import fs from 'node:fs';
import assert from 'node:assert/strict';
const [godotPath,nativePath,upstreamPath,outputPath]=process.argv.slice(2);
assert(outputPath,'Usage: node compare_production_hand_chains.mjs godot.json native.json upstream-report.json output.json');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const upstream=read(upstreamPath);
assert.equal(upstream.godotPath,godotPath); assert.equal(upstream.nativePath,nativePath);
assert.equal(upstream.failures.length,0,'Resolve upstream pose/curve divergence before checking hands');
assert.equal(upstream.excludedPoseFrames,0);
assert(upstream.stages.includes('PostAim'));
const g=read(godotPath),u=read(nativePath);
assert.equal(g.traces.length,1); assert.equal(u.traces.length,1);
const a=g.traces[0].frames,b=u.traces[0].frames;
assert.equal(a.length,b.length); assert.equal(a.length,upstream.poseFrames);
const bones=['upperarm_l','lowerarm_l','hand_l','upperarm_r','lowerarm_r','hand_r'];
const mapping=bones.map(bone=>({bone,g:g.names.findIndex(n=>n.toLowerCase()===bone),u:u.names.findIndex(n=>n.toLowerCase()===bone)}));
assert(mapping.every(m=>m.g>=0&&m.u>=0),'Missing authored arm chain');
const limits={positionCm:.001,rotationDegrees:.02,scale:.00001};
const empty=()=>({positionCm:0,rotationDegrees:0,scale:0});
const report={schemaVersion:1,godotPath,nativePath,upstreamPath,
  scope:'Six final local arm bones only; excludes full final pose, world-space targets, feet and pelvis',
  limits,frames:a.length,activeFrames:{left:0,right:0},errors:Object.fromEntries(bones.map(n=>[n,empty()])),
  nativePostAimChange:empty(),godotPostAimChange:empty(),failedFrames:0,firstFailures:[]};
const error=(p,q)=>{
  const positionCm=Math.hypot(...p.position.map((v,i)=>v-q.position[i]));
  const dot=Math.abs(p.rotation.reduce((s,v,i)=>s+v*q.rotation[i],0));
  const rotationDegrees=2*Math.acos(Math.min(1,dot/(Math.hypot(...p.rotation)*Math.hypot(...q.rotation))))*180/Math.PI;
  const scale=Math.max(...p.scale.map((v,i)=>Math.abs(v-q.scale[i])));
  assert([positionCm,rotationDegrees,scale].every(Number.isFinite));
  return {positionCm,rotationDegrees,scale};
};
const merge=(target,value)=>{for(const key of Object.keys(target))target[key]=Math.max(target[key],value[key]);};
for(let f=0;f<a.length;f++){
  const x=a[f],y=b[f]; assert.equal(x.serial,y.serial);
  assert.deepEqual(JSON.parse(JSON.stringify(x.input)),JSON.parse(JSON.stringify(y.input)));
  if(x.input.properties.Enable_HandIK_L>1e-5)report.activeFrames.left++;
  if(x.input.properties.Enable_HandIK_R>1e-5)report.activeFrames.right++;
  let failed=false;
  for(const m of mapping){
    const e=error(x.pose[m.g],y.pose[m.u]); merge(report.errors[m.bone],e);
    merge(report.godotPostAimChange,error(x.pose[m.g],x.stages.PostAim.pose[m.g]));
    merge(report.nativePostAimChange,error(y.pose[m.u],y.stages.PostAim.pose[m.u]));
    if(Object.keys(limits).some(k=>e[k]>limits[k])){
      failed=true;
      if(report.firstFailures.length<12)report.firstFailures.push({serial:x.serial,bone:m.bone,...e});
    }
  }
  if(failed)report.failedFrames++;
}
assert(report.activeFrames.left+report.activeFrames.right>0,'No actual hand IK coverage');
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
process.exitCode=report.failedFrames?1:0;

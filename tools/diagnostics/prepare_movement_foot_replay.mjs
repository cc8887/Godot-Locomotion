import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';

const [directory,skeletonCapture,outputPath]=process.argv.slice(2);
assert(outputPath,'Usage: node prepare_movement_foot_replay.mjs capture-dir skeleton-graph.json fresh-output.json');
assert(!fs.existsSync(outputPath),'Preserve existing evidence');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const rows=read(path.join(directory,'frames.json')),graph=read(path.join(directory,'graph.json'));
const skeleton=read(skeletonCapture).skeleton;
function sameCapture(a,b,label) {
  if(typeof a==='number' && typeof b==='number') {
    // graph.json expands Single to exact double; frames.json emits the shortest
    // decimal that round-trips to Single. Preserve the graph value for native use.
    assert(a===b || !Number.isInteger(a) && !Number.isInteger(b) && Math.fround(a)===Math.fround(b),label);
    return;
  }
  if(a===null || b===null || typeof a!=='object' || typeof b!=='object') { assert.equal(a,b,label); return; }
  assert.deepEqual(Object.keys(a).sort(),Object.keys(b).sort(),label);
  for(const key of Object.keys(a))sameCapture(a[key],b[key],label+'.'+key);
}
assert.deepEqual(skeleton.names,graph.names); assert.equal(graph.traces.length,1);
assert.equal(rows.length,graph.traces[0].frames.length);
// The original replay uses a 50cm half-height box. Retain the observed
// StartFloor's X/Z origin, and lower the box centre to preserve its top plane.
// Movement remains captured; no native CharacterMovement/edge equivalence claim.
const scenePath='scenes/demo/p4_locomotion_demo.tscn';
const scene=fs.readFileSync(scenePath,'utf8');
assert(scene.includes('[node name="StartFloor" type="StaticBody3D" parent="World"]\r\nposition = Vector3(-4, -0.25, 1)') ||
  scene.includes('[node name="StartFloor" type="StaticBody3D" parent="World"]\nposition = Vector3(-4, -0.25, 1)'));
let collider;
const result=rows.map((r,i)=>{
  const g=graph.traces[0].frames[i];
  assert.equal(r.Frame,i+1); assert.equal(g.serial,r.Frame);
  assert.equal(g.input.properties.MovementState,1,'Grounded replay only');
  assert.equal(r.Input.Floor.PlatformId,-1,'Do not fabricate a moving base');
  collider??=r.Input.Floor.ColliderId; assert.equal(r.Input.Floor.ColliderId,collider);
  if(i>0)for(const side of ['Left','Right']) {
    const hit=r.RefactoredStages.Response[side];
    assert(hit.Blocking && hit.ColliderIdentity===collider && Math.abs(hit.Impact.Z)<.0001);
    assert(hit.Normal.X===0 && hit.Normal.Y===0 && hit.Normal.Z===1);
  }
  sameCapture(g.motorInput,r.Input,'frame.'+r.Frame+'.MotorInput');
  return {Frame:r.Frame,Presentation:r.Presentation,Locked:r.RefactoredLocks.Left.Amount>.999 && r.RefactoredLocks.Right.Amount>.999,
    Platform:{Position:{X:-4,Y:-.5,Z:1},Rotation:{X:0,Y:0,Z:0,W:1}},
    Sole:r.Sole,RefactoredRig:r.RefactoredRig,RefactoredRigFeet:r.RefactoredRigFeet,RefactoredLocks:r.RefactoredLocks,
    RigCapture:{Skeleton:skeleton,Input:g.rigInput,Stages:g.stages,PostRigComponents:g.postRigComponents,
      MotorInput:g.motorInput,Movement:g.input,PreviousCurves:g.previousCurves,FinalCurves:g.curves,FinalPose:g.pose},
    Queries:r.RefactoredStages,Scene:r.Input.FootIk,Based:r.BasedFootLock};
});
fs.writeFileSync(outputPath,JSON.stringify(result),{flag:'wx'});
const sources=[path.join(directory,'frames.json'),path.join(directory,'graph.json'),skeletonCapture,scenePath];
fs.writeFileSync(outputPath+'.provenance.json',JSON.stringify({directory,frames:rows.length,
  scope:'Captured grounded movement and pre-rig pose; unchanged original continuous foot functions/CR_Als; enlarged equivalent horizontal collision plane, not original character simulation',
  sources:sources.map(file=>({file,sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')}))},null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify({frames:rows.length,outputPath,closedFeedbackInput:true,movingBase:false}));

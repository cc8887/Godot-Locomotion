import fs from 'node:fs';
import assert from 'node:assert/strict';

const [godotPath,nativePath,reportPath]=process.argv.slice(2);
assert(godotPath && nativePath && reportPath,'Expected Godot capture, native capture and fresh report.');
const read=path=>JSON.parse(fs.readFileSync(path,'utf8'));
const godot=read(godotPath),native=read(nativePath);
assert.equal(godot.dispatchStopNotifies,true); assert.equal(native.dispatchStopNotifies,true);
assert(godot.traces.length>0); assert.equal(native.traces.length,godot.traces.length);
const limits={positionSeconds:1e-6,weight:1e-5};
const traces=[];
let frames=0,failures=0,maxPositionSeconds=0,maxWeight=0;
for(let t=0;t<godot.traces.length;t++) {
  const a=godot.traces[t],b=native.traces[t];
  assert.equal(a.name,b.name); assert.equal(a.frames.length,b.frames.length);
  const summary={name:a.name,frames:a.frames.length,godotEvents:[],nativeEvents:[],godotActive:0,nativeActive:0,failures:0,firstFailure:null};
  let previousNative={};
  for(let i=0;i<a.frames.length;i++) {
    const x=a.frames[i],y=b.frames[i],errors=[];
    assert.equal(x.serial,i+1); assert.equal(y.serial,i+1);
    assert.equal(x.input.delta,y.input.delta);
    // Native output must have been fed back continuously by its own instance.
    assert.deepEqual(y.previousCurves,previousNative); previousNative=y.curves;
    assert(Array.isArray(x.stopNotifies) && Array.isArray(y.stopNotifies));
    for(const n of x.stopNotifies)summary.godotEvents.push({frame:x.serial,name:n});
    for(const n of y.stopNotifies)summary.nativeEvents.push({frame:y.serial,name:n});
    if(JSON.stringify(x.stopNotifies)!==JSON.stringify(y.stopNotifies))errors.push({kind:'notifications',godot:x.stopNotifies,native:y.stopNotifies});
    const p=x.montageEvaluations,q=y.montageEvaluations;
    assert(Array.isArray(p) && Array.isArray(q));
    if(p.length)summary.godotActive++;
    if(q.length)summary.nativeActive++;
    if(p.length!==q.length)errors.push({kind:'evaluationCount',godot:p.length,native:q.length});
    for(let e=0;e<Math.min(p.length,q.length);e++) {
      assert(p[e].slot==='Grounded Slot' && q[e].slot==='Grounded Slot','Unexpected montage consumer in Stop-only probe');
      if(p[e].asset!==q[e].asset)errors.push({kind:'asset',godot:p[e].asset,native:q[e].asset});
      const position=Math.abs(p[e].position-q[e].position),weight=Math.abs(p[e].weight-q[e].weight);
      assert(Number.isFinite(position) && Number.isFinite(weight));
      maxPositionSeconds=Math.max(maxPositionSeconds,position); maxWeight=Math.max(maxWeight,weight);
      if(position>limits.positionSeconds || weight>limits.weight)errors.push({kind:'evaluation',index:e,positionSeconds:position,weight});
    }
    if(errors.length){summary.failures++;failures++;summary.firstFailure??={frame:x.serial,errors};}
    frames++;
  }
  assert(summary.godotEvents.length && summary.nativeEvents.length && summary.godotActive && summary.nativeActive,
    'Both independent graph histories must actually dispatch and sample Stop');
  assert.equal(a.frames.at(-1).montageEvaluations.length,0,'Godot did not finish fading Stop');
  assert.equal(b.frames.at(-1).montageEvaluations.length,0,'Native did not finish fading Stop');
  traces.push(summary);
}
const report={scope:'Continuous full V4 graph Stop callbacks and native Montage lifecycle under controlled properties; does not verify Character motor or Refactored whole graph',
  limits,frames,failures,maxPositionSeconds,maxWeight,traces};
fs.writeFileSync(reportPath,JSON.stringify(report,null,2)+'\n',{flag:'wx'});
console.log(JSON.stringify(report));
if(failures)process.exitCode=1;

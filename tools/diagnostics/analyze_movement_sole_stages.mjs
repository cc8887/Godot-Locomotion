import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const [directory, bindingDirectory, outputPath, nativePath] = process.argv.slice(2);
assert(outputPath, 'Usage: node analyze_movement_sole_stages.mjs capture-dir binding-capture-dir report.json [native.json]');
const read = (dir, file) => JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
const rows = read(directory, 'frames.json'), graph = read(directory, 'graph.json');
const metadataPath = path.join(directory, 'capture.json');
const metadata = fs.existsSync(metadataPath) ? read(directory, 'capture.json') :
  { hz: 60, frames: 720, reversalDelaySeconds: 0, strafe: true };
const hz = metadata.hz, reversalDelay = metadata.reversalDelaySeconds;
assert([30,60,120].includes(hz) && metadata.strafe);
assert(Number.isFinite(reversalDelay) && reversalDelay >= 0 && reversalDelay <= .5);
const bindingGraph = read(bindingDirectory, 'graph.json'), bindings = read(bindingDirectory, 'sole-bindings.json');
assert.deepEqual(graph.names, bindingGraph.names);
const parents = bindingGraph.skeleton.parents, names = graph.names;
assert.equal(parents.length, names.length);
assert.equal(rows.length, hz * 12); assert.equal(metadata.frames, rows.length); assert.equal(graph.traces.length, 1);
assert.equal(graph.traces[0].frames.length, rows.length);
const points = bindings.points.map(p => ({ ...p, influences: p.influences.map(i => {
  const bone = names.findIndex(n => n.toLowerCase() === i.bone.toLowerCase());
  assert(bone >= 0); assert(i.weight > 0 && Number.isFinite(i.weight));
  assert(i.position.every(Number.isFinite));
  return { ...i, bone };
}) }));
for (const p of points) assert(Math.abs(p.influences.reduce((s, i) => s + i.weight, 0) - 1) < .001);
const v = a => [a.X,a.Y,a.Z], q = a => [a.X,a.Y,a.Z,a.W];
const add = (a,b) => a.map((x,i) => x+b[i]);
const mul = (a,b) => a.map((x,i) => x*b[i]);
const scale = (a,s) => a.map(x => x*s);
const multiply = (a,b) => [a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],
  a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0], a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],
  a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const rotate = (r,p) => multiply(multiply(r,[...p,0]),[-r[0],-r[1],-r[2],r[3]]).slice(0,3);
const transform = (t,p) => add(t.position,rotate(t.rotation,mul(t.scale,p)));
function components(locals) {
  assert.equal(locals.length, parents.length);
  return locals.reduce((out, t, i) => {
    const parent = parents[i]; assert(parent >= -1 && parent < i);
    const b = parent < 0 ? null : out[parent];
    const rotation = b ? multiply(b.rotation,t.rotation) : t.rotation;
    const norm = Math.hypot(...rotation); assert(Math.abs(norm-1) < .001);
    out.push({ position:b ? transform(b,t.position) : t.position,
      rotation:scale(rotation,1/norm),scale:b ? mul(b.scale,t.scale) : t.scale });
    return out;
  },[]);
}
function skin(poses, world) {
  return points.map(p => {
    let sum=[0,0,0];
    for (const influence of p.influences) sum=add(sum,scale(transform(poses[influence.bone],influence.position),influence.weight));
    return transform(world,sum).map(x=>x*.01); // Native world meters; Z is up.
  });
}
const minimum = (vertices,left) => Math.min(...vertices.filter((_,i)=>points[i].left===left).map(p=>p[2]));
const window = f => {
  const t=f/hz;
  return t<=1?'idle':t<=1.5?'start':t<=4+reversalDelay?'right':t<=5+reversalDelay?'reverse_left':
    t<=7+reversalDelay?'left':t<=8+reversalDelay?'reverse_right':t<=10?'right_again':t<=11?'stop':'turn';
};
const report = { directory,bindingDirectory,frames:rows.length,hz,reversalDelaySeconds:reversalDelay,
  reversalInputs:[],pointCounts:{left:points.filter(p=>p.left).length,right:points.filter(p=>!p.left).length},
  scope:'Actual mesh stage attribution and 5mm penetration gate; near-plane material point motion is geometric, not a support-force/slip verdict',
  maximumFinalSkinErrorMeters:0,waitingForFeetFrames:0,waitingIntervals:[],hipStarts:[],windows:{},worst:[],nearPlanePointMotion:{},passed:true };
const stageNames=['MainMovement','BaseLayer','PostLayering','PostAim'];
const native = nativePath ? JSON.parse(fs.readFileSync(nativePath,'utf8')) : null;
let nativeRows, nativeMap;
if(native) {
  assert.equal(native.traces.length,1);
  assert(native.traces[0].frames.length>=rows.length);
  nativeRows=native.traces[0].frames;
  nativeMap=names.map(n=>native.names.findIndex(m=>m.toLowerCase()===n.toLowerCase()));
  assert(nativeMap.every(i=>i>=0));
  report.nativeComparison={nativePath,frames:rows.length,totalNativeFrames:nativeRows.length,
    scope:'Matching input prefix and four pre-foot local pose stages; same imported mesh applied offline, not native final feet or whole-character parity',
    maximumPositionCm:0,maximumRotationDegrees:0,maximumScale:0,maximumSoleDistanceMeters:0,passed:true};
}
let previous=null;
let waitingStart=0;
const all=[];
for(let i=0;i<rows.length;i++) {
  const row=rows[i],g=graph.traces[0].frames[i]; assert.equal(row.Frame,i+1); assert.equal(g.serial,row.Frame);
  assert(Math.abs(row.Input.DeltaTime-1/hz)<1e-7);
  const tick=Math.fround(row.Frame*60/hz), offset=Math.fround(Math.fround(reversalDelay)*60);
  const expectedX=tick<=60 || tick>600?0:tick<=Math.fround(240+offset)?1:tick<=Math.fround(420+offset)?-1:1;
  // Negating the authored left vector produces IEEE -0 for Y; it is still no input.
  assert(row.Input.Command.MovementAxes.X===expectedX,'Movement schedule does not match physical time');
  assert(row.Input.Command.MovementAxes.Y===0);
  if(i>0 && expectedX!==0 && rows[i-1].Input.Command.MovementAxes.X===-expectedX)
    report.reversalInputs.push({frame:row.Frame,seconds:row.Frame/hz,axis:expectedX,
      previousPhase:rows[i-1].Cycle.Phase,previousCrossing:rows[i-1].Cycle.Crossing});
  const w=g.rigInput.ToWorld,world={position:v(w.Position),rotation:q(w.Rotation),scale:v(w.Scale)};
  if (i>0) for(const side of ['Left','Right']) {
    const hit=row.RefactoredStages.Response[side];
    assert(hit.Blocking && hit.ColliderIdentity===row.Input.Floor.ColliderId);
    assert(Math.abs(hit.Impact.Z)<.0001); assert(v(hit.Normal).every((value,index)=>value===[0,0,1][index]));
  }
  const final=skin(g.postRigComponents,world);
  const stageVertices=Object.fromEntries(stageNames.map(s=>[s,skin(components(g.stages[s].pose),world)]));
  if(native) {
    const n=nativeRows[i]; assert.equal(n.serial,g.serial);
    assert.deepEqual(JSON.parse(JSON.stringify(g.input)),JSON.parse(JSON.stringify(n.input)));
    const comparison=report.nativeComparison;
    for(const s of stageNames) {
      const np=nativeMap.map(index=>n.stages[s].pose[index]),gp=g.stages[s].pose;
      for(let bone=0;bone<gp.length;bone++) {
        const a=gp[bone],b=np[bone];
        comparison.maximumPositionCm=Math.max(comparison.maximumPositionCm,Math.hypot(...a.position.map((x,j)=>x-b.position[j])));
        const dot=a.rotation.reduce((sum,x,j)=>sum+x*b.rotation[j],0)/(Math.hypot(...a.rotation)*Math.hypot(...b.rotation));
        comparison.maximumRotationDegrees=Math.max(comparison.maximumRotationDegrees,2*Math.acos(Math.min(1,Math.abs(dot)))*180/Math.PI);
        comparison.maximumScale=Math.max(comparison.maximumScale,...a.scale.map((x,j)=>Math.abs(x-b.scale[j])));
      }
      const nv=skin(components(np),world);
      for(const left of [true,false]) comparison.maximumSoleDistanceMeters=Math.max(comparison.maximumSoleDistanceMeters,
        Math.abs(minimum(nv,left)-minimum(stageVertices[s],left)));
    }
  }
  const key=window(row.Frame),group=report.windows[key]??={frames:0,left:{minimumMeters:Infinity,penetratingFrames:0},right:{minimumMeters:Infinity,penetratingFrames:0}};
  group.frames++;
  if(row.Cycle.WaitingForFeet) report.waitingForFeetFrames++;
  if(row.Cycle.WaitingForFeet && waitingStart===0) waitingStart=row.Frame;
  if(!row.Cycle.WaitingForFeet && waitingStart!==0) {
    report.waitingIntervals.push({start:waitingStart,end:row.Frame-1,seconds:(row.Frame-waitingStart)/hz});
    waitingStart=0;
  }
  if(row.Cycle.HipTransition && row.Cycle.TransitionsStartedThisFrame>0) {
    assert.equal(row.Cycle.Crossing,0);
    report.hipStarts.push({frame:row.Frame,crossing:row.Cycle.Crossing,hipBias:row.Cycle.HipBias,direction:row.Cycle.Direction});
  }
  for(const [side,left] of [['Left',true],['Right',false]]) {
    assert.equal(row.Sole.FullSupport[side+'Count'],report.pointCounts[side.toLowerCase()]);
    const observed=row.Sole.FullSupport[side+'MinimumHeight'];
    report.maximumFinalSkinErrorMeters=Math.max(report.maximumFinalSkinErrorMeters,Math.abs(minimum(final,left)-observed));
    const stages=Object.fromEntries(stageNames.map(s=>[s,minimum(stageVertices[s],left)]));
    const item={frame:row.Frame,side,window:key,observed,stages,lockAmount:row.RefactoredLocks[side].Amount,
      speed:row.Movement.Speed,stride:row.Cycle.Stride,playRate:row.Cycle.PlayRate,phase:row.Cycle.Phase};
    if(row.Presentation.IsVisible) {
      all.push(item); const state=group[side.toLowerCase()]; state.minimumMeters=Math.min(state.minimumMeters,observed);
      if(observed<-.005){state.penetratingFrames++;report.passed=false;}
    }
    if(previous && row.Presentation.IsVisible) {
      const metric=report.nearPlanePointMotion[key]??={pairs:0,maximumSpeedMetersPerSecond:0,maximum:null};
      for(let p=0;p<points.length;p++) {
        if(points[p].left!==left || Math.abs(final[p][2])>.002 || Math.abs(previous[p][2])>.002)continue;
        const speed=Math.hypot(final[p][0]-previous[p][0],final[p][1]-previous[p][1])/row.Input.DeltaTime;
        metric.pairs++;
        if(speed>metric.maximumSpeedMetersPerSecond){metric.maximumSpeedMetersPerSecond=speed;metric.maximum={frame:row.Frame,side,point:p,lockAmount:item.lockAmount};}
      }
    }
  }
  previous=final;
}
assert(report.maximumFinalSkinErrorMeters<.0001,'Offline material points do not reproduce actual Godot skinning within 0.1mm');
assert.equal(report.reversalInputs.length,2,'Both physical reversal inputs must be present');
if(waitingStart) report.waitingIntervals.push({start:waitingStart,end:rows.length,seconds:(rows.length-waitingStart+1)/hz,open:true});
report.worst=all.sort((a,b)=>a.observed-b.observed).slice(0,12);
if(native) {
  const c=report.nativeComparison;
  c.passed=c.maximumPositionCm<=.001 && c.maximumRotationDegrees<=.02 && c.maximumScale<=.00001;
  report.passed &&= c.passed;
}
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({passed:report.passed,maximumFinalSkinErrorMeters:report.maximumFinalSkinErrorMeters,
  waitingForFeetFrames:report.waitingForFeetFrames,hipStarts:report.hipStarts,nativeComparison:report.nativeComparison,
  worst:report.worst.slice(0,3),windows:report.windows}));
if(!report.passed)process.exitCode=1;

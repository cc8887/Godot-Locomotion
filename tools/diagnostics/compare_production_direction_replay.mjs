// Older V4 probes dispatch Stop only. Explicitly bound pose parity at the first
// unsupported montage (which can also interrupt Stop); retain all version-only
// curves and excluded frames.
import fs from 'node:fs';
import assert from 'node:assert/strict';
const [godotPath,nativePath,outputPath,coverage]=process.argv.slice(2);
assert(outputPath,'Usage: node compare_production_direction_replay.mjs godot.json native.json report.json');
assert(!coverage||['--require-turn-complete','--require-rotate','--require-armed'].includes(coverage),'Unknown coverage requirement');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const g=read(godotPath),u=read(nativePath);
assert.equal(g.traces.length,1); assert.equal(u.traces.length,1);
const a=g.traces[0].frames,b=u.traces[0].frames;
assert.equal(a.length,b.length); assert.equal(g.traces[0].name,u.traces[0].name);
const mapping=g.names.map(n=>u.names.findIndex(m=>m.toLowerCase()===n.toLowerCase()));
assert(mapping.every(i=>i>=0));
const extras=new Set(['FootLeftIk','FootLeftLock','FootRightIk','FootRightLock','PoseGrounded','PoseMoving']);
const observed=new Set();
assert.equal(g.runIdleControls??false,u.runIdleControls??false);
const idleControls=g.runIdleControls===true;
const stages=u.captureUpperStages===true?['MainMovement','BaseLayer','PostLayering','PostAim']:['MainMovement','BaseLayer'];
const supported=new Set(idleControls?['Grounded Slot','(N) Turn/Rotate','(CLF) Turn/Rotate']:['Grounded Slot']);
const boundary=a.findIndex((f,i)=>[...f.montageEvaluations,...b[i].montageEvaluations].some(m=>!supported.has(m.slot)));
const end=boundary<0?a.length:boundary;
const report={schemaVersion:1,godotPath,nativePath,
  scope:'Listed stage poses, shared V4 curves and supported montage playback before unsupported montage. Not full final-pose or complete curve parity.',
  stages,stageErrors:Object.fromEntries(stages.map(s=>[s,{positionCm:0,rotationDegrees:0,curve:0}])),
  idleControls,supportedSlots:[...supported],maxIdleControlError:0,montageEvaluations:0,maxMontageTimeError:0,maxMontageWeightError:0,
  frames:a.length,poseFrames:end,excludedPoseFrames:a.length-end,
  firstUnsupportedMontage:boundary<0?null:{serial:a[boundary].serial,montages:a[boundary].montageEvaluations.filter(m=>!supported.has(m.slot))},
  limits:{positionCm:.001,rotationDegrees:.02,scale:.00001,curve:.0001,idleNumber:1e-5,montageSeconds:1e-6,montageWeight:1e-5},
  maxPositionCm:0,maxRotationDegrees:0,maxScale:0,maxSharedCurve:0,stopNotifies:[],groundedEvaluations:0,excludedGroundedMismatchFrames:[],failures:[]};
const fail=(serial,kind,details)=>{if(report.failures.length<20)report.failures.push({serial,kind,...details});};
const range=values=>{
  const numbers=values.filter(v=>typeof v==='number');
  assert(numbers.every(Number.isFinite));
  return {presentFrames:numbers.length,min:numbers.length?Math.min(...numbers):null,max:numbers.length?Math.max(...numbers):null};
};
report.inputCoverage=Object.fromEntries(['Enable_AimOffset','Arm_L_Add','Arm_R_Add','Arm_L_LS','Arm_R_LS','Enable_HandIK_L','Enable_HandIK_R']
  .map(k=>[k,range(a.map(f=>f.input.properties[k]))]));
report.spineCurveCoverage=range(a.map(f=>f.previousCurves.Enable_SpineRotation));
report.rotationModes=[...new Set(a.map(f=>f.input.properties.RotationMode))];
report.overlayStates=[...new Set(a.map(f=>f.input.properties.OverlayState))];
for(let f=0;f<a.length;f++){
  const x=a[f],y=b[f];assert.equal(x.serial,y.serial);
  // JSON encoding canonicalizes signed zero. All nonzero values remain exact.
  assert.deepEqual(JSON.parse(JSON.stringify(x.input)),JSON.parse(JSON.stringify(y.input)));
  if(idleControls){
    assert(x.idleControl&&y.idleControl);
    assert.deepEqual(Object.keys(x.idleControl).sort(),Object.keys(y.idleControl).sort());
    for(const key of Object.keys(x.idleControl)){
      const v=x.idleControl[key],w=y.idleControl[key];
      if(typeof v==='boolean'){if(v!==w)fail(x.serial,'idleBoolean',{key,godot:v,native:w});}
      else {
        const error=Math.abs(v-w);assert(Number.isFinite(error));report.maxIdleControlError=Math.max(report.maxIdleControlError,error);
        if(error>1e-5)fail(x.serial,'idleNumber',{key,godot:v,native:w,error});
      }
    }
  }
  if(JSON.stringify(x.stopNotifies)!==JSON.stringify(y.stopNotifies))fail(x.serial,'stopNotify',{godot:x.stopNotifies,native:y.stopNotifies});
  if(x.stopNotifies.length)report.stopNotifies.push({serial:x.serial,names:x.stopNotifies});
  const gm=x.montageEvaluations.filter(m=>m.slot==='Grounded Slot');
  const um=y.montageEvaluations.filter(m=>m.slot==='Grounded Slot');
  if(f>=end){
    if(JSON.stringify(gm)!==JSON.stringify(um))report.excludedGroundedMismatchFrames.push(x.serial);
    continue;
  }
  report.groundedEvaluations+=gm.length;
  const ge=x.montageEvaluations,ue=y.montageEvaluations;
  if(ge.length!==ue.length)fail(x.serial,'montageCount',{godot:ge.length,native:ue.length});
  for(let n=0;n<Math.min(ge.length,ue.length);n++){
    const p=ge[n],q=ue[n];report.montageEvaluations++;
    if(p.asset!==q.asset||p.slot!==q.slot)fail(x.serial,'montageAsset',{godot:p,native:q});
    const time=Math.abs(p.position-q.position),weight=Math.abs(p.weight-q.weight);
    assert(Number.isFinite(time)&&Number.isFinite(weight));
    report.maxMontageTimeError=Math.max(report.maxMontageTimeError,time);report.maxMontageWeightError=Math.max(report.maxMontageWeightError,weight);
    if(time>1e-6||weight>1e-5)fail(x.serial,'montageTiming',{time,weight});
  }
  for(const stage of stages){
    const p=x.stages[stage],q=y.stages[stage];assert.equal(q.evaluations,1);
    assert(p&&q,'Missing requested stage');
    const stageError=report.stageErrors[stage];
    for(let i=0;i<mapping.length;i++){
      const v=p.pose[i],w=q.pose[mapping[i]];
      const distance=Math.hypot(...v.position.map((n,j)=>n-w.position[j]));
      const dot=Math.abs(v.rotation.reduce((s,n,j)=>s+n*w.rotation[j],0));
      const angle=2*Math.acos(Math.min(1,dot/(Math.hypot(...v.rotation)*Math.hypot(...w.rotation))))*180/Math.PI;
      const scale=Math.max(...v.scale.map((n,j)=>Math.abs(n-w.scale[j])));
      assert([distance,angle,scale].every(Number.isFinite),'Nonfinite pose comparison');
      report.maxPositionCm=Math.max(report.maxPositionCm,distance);
      report.maxRotationDegrees=Math.max(report.maxRotationDegrees,angle);report.maxScale=Math.max(report.maxScale,scale);
      stageError.positionCm=Math.max(stageError.positionCm,distance);
      stageError.rotationDegrees=Math.max(stageError.rotationDegrees,angle);
      if(distance>.001||angle>.02||scale>.00001)fail(x.serial,'pose',{stage,bone:g.names[i],distance,angle,scale});
    }
    for(const key of new Set([...Object.keys(p.curves),...Object.keys(q.curves)])){
      if(extras.has(key)&&!Object.hasOwn(q.curves,key)){observed.add(key);continue;}
      if(!Object.hasOwn(p.curves,key)||!Object.hasOwn(q.curves,key)){fail(x.serial,'curvePresence',{stage,key});continue;}
      const error=Math.abs(p.curves[key]-q.curves[key]);report.maxSharedCurve=Math.max(report.maxSharedCurve,error);
      stageError.curve=Math.max(stageError.curve,error);
      assert(Number.isFinite(error),'Nonfinite curve comparison');
      if(error>.0001)fail(x.serial,'curve',{stage,key,error});
    }
  }
}
report.godotOnlyCurvesNotCompared=[...observed].sort();
const lifecycle=frames=>{
  const turn=frames.filter(f=>f.montageEvaluations.some(m=>m.slot==='(N) Turn/Rotate'||m.slot==='(CLF) Turn/Rotate'));
  return {firstTurn:turn[0]?.serial??null,lastTurn:turn.at(-1)?.serial??null,turnFrames:turn.length,
    finalMontages:frames.at(-1).montageEvaluations,settledFrames:turn.length?frames.length-turn.at(-1).serial:0};
};
report.turnLifecycle={godot:lifecycle(a),native:lifecycle(b),completionRequired:coverage==='--require-turn-complete'};
report.rotateFrames={godot:a.filter(f=>f.idleControl?.Rotate_L||f.idleControl?.Rotate_R).length,
  native:b.filter(f=>f.idleControl?.Rotate_L||f.idleControl?.Rotate_R).length};
if(coverage==='--require-armed'){
  assert(stages.includes('PostAim')&&report.overlayStates.some(v=>v!==0),'Missing equipped upper stage coverage');
  assert(report.inputCoverage.Enable_HandIK_L.max>0||report.inputCoverage.Enable_HandIK_R.max>0,'No active hand IK input');
  assert(report.spineCurveCoverage.max>0,'No active authored spine curve');
  assert.equal(report.excludedPoseFrames,0,'Armed replay contains unsupported playback');
}
if(coverage==='--require-rotate'){
  assert(idleControls&&report.rotateFrames.godot>0&&report.rotateFrames.native>0,'Missing original Rotate control coverage');
  assert.equal(report.excludedPoseFrames,0,'Rotate contains unsupported playback');
}
if(coverage==='--require-turn-complete'){
  assert(idleControls,'Turn completion requires original idle controls');
  assert(report.turnLifecycle.godot.turnFrames>0&&report.turnLifecycle.native.turnFrames>0,'Missing turn playback');
  assert.equal(report.turnLifecycle.godot.finalMontages.length,0,'Godot montage did not finish');
  assert.equal(report.turnLifecycle.native.finalMontages.length,0,'Native montage did not finish');
  assert.equal(report.excludedPoseFrames,0,'Complete turn contains unsupported playback');
}
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));process.exitCode=report.failures.length?1:0;

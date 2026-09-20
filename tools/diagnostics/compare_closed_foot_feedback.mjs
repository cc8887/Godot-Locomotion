import fs from 'node:fs';
import assert from 'node:assert/strict';
const [capturePath,nativePath,outputPath] = process.argv.slice(2);
assert(outputPath,'Usage: node compare_closed_foot_feedback.mjs capture.json native.json report.json');
const read = p => JSON.parse(fs.readFileSync(p,'utf8'));
const rows=read(capturePath), native=read(nativePath);
assert.equal(native.schemaVersion,2); assert.equal(native.closedFeedback,true);
assert.equal(rows.length,native.frames.length); assert(rows.length>1);
const v=o=>[o.X,o.Y,o.Z], q=o=>[o.X,o.Y,o.Z,o.W];
const pose=o=>({position:v(o.Position),rotation:q(o.Rotation),scale:v(o.Scale)});
const multiply=(a,b)=>[
  a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],
  a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const normalized=q=>{const n=Math.hypot(...q);assert(n>0);return q.map(v=>v/n);};
const compose=(a,b)=>{
  const r=b.rotation, p=multiply(multiply(r,[...a.position.map((v,i)=>v*b.scale[i]),0]),[-r[0],-r[1],-r[2],r[3]]);
  return {position:b.position.map((v,i)=>v+p[i]),rotation:normalized(multiply(r,a.rotation)),scale:a.scale.map((v,i)=>v*b.scale[i])};
};
const consumed=['FootLeftIk','FootRightIk','FootLeftLock','FootRightLock','PoseGrounded','PoseInAir'];
const maxima={},failures=[],counts={valid:0,lockedLeft:0,lockedRight:0}; let failureCount=0;
function check(frame,field,error,limit){
  assert(Number.isFinite(error));
  if(!maxima[field]||error>maxima[field].error)maxima[field]={frame,error,limit};
  if(error>limit){failureCount++;if(failures.length<20)failures.push({frame,field,error,limit});}
}
function comparePose(frame,label,a,b){
  assert([a,b].every(p=>[...p.position,...p.rotation].every(Number.isFinite)));
  check(frame,label+'.positionCm',Math.hypot(...a.position.map((v,i)=>v-b.position[i])),.001);
  const na=normalized(a.rotation),nb=normalized(b.rotation),dot=Math.abs(na.reduce((s,v,i)=>s+v*nb[i],0));
  check(frame,label+'.rotationDegrees',2*Math.acos(Math.min(1,dot))*180/Math.PI,.02);
}
for(let f=0;f<rows.length;f++){
  const row=rows[f], result=native.frames[f], input=row.RigCapture.Input, trace=result.feedback;
  assert.equal(row.Frame,f+1);assert.equal(result.frame,row.Frame); assert(trace);
  check(row.Frame,'valid',Number(trace.valid!==input.FootTransformsValid),0);
  check(row.Frame,'becameValid',Number(trace.becameValid!==(input.FootTransformsValid&&!(f>0&&rows[f-1].RigCapture.Input.FootTransformsValid))),0);
  check(row.Frame,'pelvisAmount',Math.abs(trace.pelvisAmount-input.PelvisAmount),1e-4);
  if(trace.baseIdentity){
    const p=row.Platform.Position,r=row.Platform.Rotation;
    comparePose(row.Frame,'sampledBaseAgainstActualPlatform',
      {position:[-p.Z*100,p.X*100,p.Y*100],rotation:[r.Z,-r.X,-r.Y,r.W]},trace.base);
  }
  if(trace.valid)counts.valid++;
  for(const name of consumed){
    // These native consumers call GetCurveValue, whose missing-value behavior
    // is zero. This compares consumed values, not every curve's presence.
    check(row.Frame,'previousCurve.'+name,Math.abs((trace.previousCurves[name]??0)-(row.RigCapture.PreviousCurves[name]??0)),1e-4);
    check(row.Frame,'finalCurve.'+name,Math.abs((result.finalCurves[name]??0)-(row.RigCapture.FinalCurves[name]??0)),1e-4);
  }
  for(const [side,key] of [['Left','left'],['Right','right']]){
    const expected=row.RefactoredLocks[side],actual=trace[key];
    if(actual.amount>.999)counts['locked'+side]++;
    check(row.Frame,side+'.amount',Math.abs(actual.amount-expected.Amount),1e-6);
    for(const [a,b] of [['worldLock','WorldLock'],['baseLock','BaseLock'],['componentLock','ComponentLock'],['finalComponent','FinalComponent']])
      comparePose(row.Frame,side+'.'+a,pose(expected[b]),actual[a]);
    // Invalid first-frame targets are not consumed by RefreshFeet/Rig.
    if(input.FootTransformsValid&&f>0){
      const previous=pose(rows[f-1].RefactoredLocks[side+'Target']);
      comparePose(row.Frame,side+'.sampledTargetWorld',compose(previous,pose(input.ToWorld)),actual.targetWorld);
    }
  }
  if(input.FootTransformsValid&&f>0){
    const expected=q(rows[f-1].RefactoredLocks.PelvisRotation);
    comparePose(row.Frame,'sampledPelvisRotation',{position:[0,0,0],rotation:expected},{position:[0,0,0],rotation:trace.pelvis.rotation});
  }
}
assert(counts.valid>0 && counts.lockedLeft>0 && counts.lockedRight>0,'No actual valid/locked feedback coverage');
const report={capturePath,nativePath,frames:rows.length,counts,failureCount,firstFailures:failures,maxima,
  passed:failureCount===0,scope:'Continuous native foot state, socket samples and six consumed curve values against actual Godot history; movement and pre-rig animation remain captured; no whole-character or skin-contact acceptance claim'};
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({frames:report.frames,counts,failureCount,firstFailures:failures,passed:report.passed}));
if(!report.passed)process.exitCode=1;

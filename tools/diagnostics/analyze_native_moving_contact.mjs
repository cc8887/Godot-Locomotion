import fs from 'node:fs';
import assert from 'node:assert/strict';
const [capturePath,nativePath,outputPath]=process.argv.slice(2);
assert(outputPath,'Usage: node analyze_native_moving_contact.mjs capture.json native.json report.json');
const rows=JSON.parse(fs.readFileSync(capturePath,'utf8'));
const native=JSON.parse(fs.readFileSync(nativePath,'utf8'));
assert(native.closedFeedback && native.schemaVersion===2 && native.frames.length===rows.length);
const report={capturePath,nativePath,frames:rows.length,
  scope:'Original continuous foot functions and CR_Als, captured V4 pre-rig animation; native 83-point lower sole can prove penetration, not complete support acceptance',
  sides:{},contactPassed:true};
for(const [side,key] of [['Left','left'],['Right','right']]) {
  const result=report.sides[side]={visibleFrames:0,godotMinimumMeters:Infinity,nativeMinimumMeters:Infinity,
    maximumMinimumHeightDifferenceMeters:0,godotPenetratingFrames:0,nativePenetratingFrames:0,worst:null};
  for(let f=0;f<rows.length;f++) {
    const a=rows[f],b=native.frames[f]; assert.equal(a.Frame,f+1); assert.equal(a.Frame,b.frame);
    assert(b.sole.bindErrorCm<=.01);
    if(!a.Presentation.IsVisible)continue;
    const g=a.Sole[side+'MinimumHeight'],u=b.sole[key+'MinimumCm']*.01;
    assert(Number.isFinite(g)&&Number.isFinite(u));
    result.visibleFrames++;
    result.godotMinimumMeters=Math.min(result.godotMinimumMeters,g);
    result.maximumMinimumHeightDifferenceMeters=Math.max(result.maximumMinimumHeightDifferenceMeters,Math.abs(g-u));
    if(u<result.nativeMinimumMeters){result.nativeMinimumMeters=u;result.worst={frame:a.Frame,godot:g,native:u,lockAmount:b.feedback[key].amount};}
    if(g<-.005)result.godotPenetratingFrames++;
    if(u<-.005)result.nativePenetratingFrames++;
  }
  assert(result.visibleFrames>0);
  report.contactPassed &&= result.godotPenetratingFrames===0 && result.nativePenetratingFrames===0;
}
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report));
if(!report.contactPassed)process.exitCode=1;

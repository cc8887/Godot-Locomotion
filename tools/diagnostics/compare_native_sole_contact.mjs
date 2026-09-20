import fs from 'node:fs';
import assert from 'node:assert/strict';
const [capturePath,nativePath,outputPath]=process.argv.slice(2);
assert(outputPath,'Usage: node compare_native_sole_contact.mjs capture.json native.json report.json');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const rows=read(capturePath),native=read(nativePath);
assert.equal(native.closedFeedback,true);assert.equal(native.frames.length,rows.length);
const report={capturePath,nativePath,frames:rows.length,toleranceCm:.01,
  scope:'Original UE CPU-skinned fixed lower foot vertices versus imported Godot mesh samples; bidirectional nearest-point geometry check, not vertex-index identity or support-force acceptance',sides:{}};
const distance=(a,b)=>Math.hypot(...a.map((v,i)=>v-b[i]));
for(const [side,key] of [['Left','left'],['Right','right']]){
  const r=report.sides[side]={godotPoints:rows[0].Sole[side].length,nativePoints:native.frames[0].sole[key].length,
    maximumNearestDistanceCm:0,maximumMinimumHeightDifferenceCm:0,worstFrame:0,
    nativeLockedMinimumCm:Infinity,godotLockedMinimumCm:Infinity,
    nativePenetratingLockedFrames:0,godotPenetratingLockedFrames:0,lockedFrames:0};
  assert(r.godotPoints>0&&r.nativePoints>0);
  for(let f=0;f<rows.length;f++){
    const a=rows[f],b=native.frames[f];assert.equal(a.Frame,b.frame);
    const g=a.Sole[side].map(p=>[-p.Z*100,p.X*100,p.Y*100]),u=b.sole[key];
    assert.equal(g.length,r.godotPoints);assert.equal(u.length,r.nativePoints);
    assert(u.every(p=>p.length===3&&p.every(Number.isFinite)));assert(b.sole.bindErrorCm<=.01);
    let error=0;
    for(const [points,other] of [[g,u],[u,g]])
      for(const p of points)error=Math.max(error,Math.min(...other.map(q=>distance(p,q))));
    if(error>r.maximumNearestDistanceCm){r.maximumNearestDistanceCm=error;r.worstFrame=a.Frame;}
    const ug=Math.min(...u.map(p=>p[2])),gg=Math.min(...g.map(p=>p[2]));
    assert(Math.abs(ug-b.sole[key+'MinimumCm'])<1e-10);
    r.maximumMinimumHeightDifferenceCm=Math.max(r.maximumMinimumHeightDifferenceCm,Math.abs(ug-gg));
    if(a.Locked){
      r.lockedFrames++;
      assert(b.feedback[key].amount>.999,'Expected actually locked native window');
      r.nativeLockedMinimumCm=Math.min(r.nativeLockedMinimumCm,ug);
      r.godotLockedMinimumCm=Math.min(r.godotLockedMinimumCm,gg);
      if(ug<-.5)r.nativePenetratingLockedFrames++;
      if(gg<-.5)r.godotPenetratingLockedFrames++;
    }
  }
  assert(r.lockedFrames>0);
  r.geometryPassed=r.maximumNearestDistanceCm<=report.toleranceCm;
}
report.geometryPassed=Object.values(report.sides).every(s=>s.geometryPassed);
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');console.log(JSON.stringify(report));
if(!report.geometryPassed)process.exitCode=1;

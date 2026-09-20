import fs from 'node:fs';
import assert from 'node:assert/strict';
const [inputPath,outputPath]=process.argv.slice(2);
assert(outputPath,'Usage: node analyze_sole_contact_capture.mjs platform.json report.json');
const rows=JSON.parse(fs.readFileSync(inputPath,'utf8'));
assert(rows.length>0);
const proximity=.002; // Geometric candidate band, not a physical support/acceptance threshold.
const report={schemaVersion:1,inputPath,frames:rows.length,lockedFrames:0,nearPlaneBandMeters:proximity,
  scope:'Fixed lower bind-pose mesh samples against actual platform top plane; no support-force or full-surface acceptance claim',
  bindErrorMeters:0,sides:{}};
for(const side of ['Left','Right']){
  const result=report.sides[side]={samples:rows[0].Sole[side].length,allMinimumHeightMeters:Infinity,
    lockedMinimumHeightMeters:Infinity,lockedMaximumLowestHeightMeters:-Infinity,
    penetratingLockedFrames:0,nearPlaneLockedFrames:0,nearPlaneMaterialPointPairs:0,
    maximumNearPlanePointStepMeters:0,maximumPointStep:null};
  assert(result.samples>0);
  for(let i=0;i<rows.length;i++){
    const row=rows[i],sole=row.Sole,points=sole[side];
    assert.equal(row.Frame,i+1);assert.equal(points.length,result.samples);
    assert(sole.BindErrorMeters<=.0001); report.bindErrorMeters=Math.max(report.bindErrorMeters,sole.BindErrorMeters);
    assert(points.every(p=>[p.X,p.Y,p.Z].every(Number.isFinite)));
    const low=Math.min(...points.map(p=>p.Y));
    assert(Math.abs(low-sole[side+'MinimumHeight'])<1e-7);
    result.allMinimumHeightMeters=Math.min(result.allMinimumHeightMeters,low);
    if(!row.Locked)continue;
    if(side==='Left')report.lockedFrames++;
    result.lockedMinimumHeightMeters=Math.min(result.lockedMinimumHeightMeters,low);
    result.lockedMaximumLowestHeightMeters=Math.max(result.lockedMaximumLowestHeightMeters,low);
    if(low<-.005)result.penetratingLockedFrames++; // Report >5 mm penetration separately from numerical noise.
    if(points.some(p=>Math.abs(p.Y)<=proximity))result.nearPlaneLockedFrames++;
    if(!i||!rows[i-1].Locked)continue;
    for(let point=0;point<points.length;point++){
      const p=points[point],before=rows[i-1].Sole[side][point];
      if(Math.abs(p.Y)>proximity||Math.abs(before.Y)>proximity)continue;
      result.nearPlaneMaterialPointPairs++;
      const step=Math.hypot(p.X-before.X,p.Z-before.Z);
      if(step>result.maximumNearPlanePointStepMeters){
        result.maximumNearPlanePointStepMeters=step;result.maximumPointStep={frame:row.Frame,point};
      }
    }
  }
  if(!report.lockedFrames){result.lockedMinimumHeightMeters=null;result.lockedMaximumLowestHeightMeters=null;}
}
assert(report.lockedFrames>0,'No locked observation window');
fs.writeFileSync(outputPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));

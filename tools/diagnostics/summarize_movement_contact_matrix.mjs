import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const [prefix, output] = process.argv.slice(2);
assert(output && /^[a-zA-Z0-9_-]+$/.test(prefix), 'Usage: node summarize_movement_contact_matrix.mjs artifact-prefix output.json');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const summary={prefix,cases:[],frames:0,screenshots:0,maximumPenetrationMm:0,failedCases:[],passed:true,
  scope:'18 actual rendered flat-strafe cases: 30/60/120 Hz, walk/run, three reversal input times; not terrain, material support slip, whole ALS or performance certification'};
for(const hz of [30,60,120]) for(const gait of ['walk','run']) {
  const phases=[];
  for(const delay of [0,.15,.3]) {
    const label=`${prefix}-${hz}-${gait}-${String(delay).replace('.','_')}`, dir=path.join('artifacts',label);
    const r=read(`${dir}-report.json`), metadata=read(path.join(dir,'capture.json'));
    assert.equal(metadata.hz,hz); assert.equal(metadata.frames,hz*12); assert(Math.abs(metadata.reversalDelaySeconds-delay)<1e-6);
    assert.equal(metadata.run,gait==='run'); assert.equal(r.frames,metadata.frames); assert.equal(r.hz,hz);
    const violations=read(path.join(dir,'violations.json'));
    assert.equal(violations.length,0,'Captured angular-speed gate failed');
    assert(fs.readFileSync(`${dir}.log`,'utf8').includes('P4_MOVEMENT_VISUAL_OK'));
    const screenshots=fs.readdirSync(dir).filter(n=>/^frame-\d+\.png$/.test(n)).length;
    assert.equal(screenshots,120); assert.equal(r.reversalInputs.length,2);
    phases.push(r.reversalInputs[0].previousPhase);
    const worstMm=Math.max(0,-r.worst[0].observed*1000);
    summary.cases.push({label,hz,gait,delay,frames:r.frames,screenshots,passed:r.passed,maximumPenetrationMm:worstMm,
      worst:r.worst[0],waitingIntervals:r.waitingIntervals,reversalInputs:r.reversalInputs,hipStarts:r.hipStarts,
      maximumFootAngularSpeedDegrees:metadata.maximumFootAngularSpeedDegrees});
    summary.frames+=r.frames; summary.screenshots+=screenshots;
    summary.maximumPenetrationMm=Math.max(summary.maximumPenetrationMm,worstMm);
    if(!r.passed){summary.failedCases.push(label);summary.passed=false;}
  }
  // Input delays must actually exercise different cycle phases, not just
  // lengthen an idle period while leaving the movement clock identical.
  for(let i=0;i<phases.length;i++)for(let j=i+1;j<phases.length;j++) {
    const separation=Math.abs(phases[i]-phases[j]);
    assert(Math.min(separation,1-separation)>.05,'Reversal phase coverage is redundant');
  }
}
assert.equal(summary.cases.length,18);
fs.writeFileSync(output,JSON.stringify(summary,null,2)+'\n');
console.log(JSON.stringify({prefix,passed:summary.passed,frames:summary.frames,screenshots:summary.screenshots,
  maximumPenetrationMm:summary.maximumPenetrationMm,failedCases:summary.failedCases}));
if(!summary.passed)process.exitCode=1;

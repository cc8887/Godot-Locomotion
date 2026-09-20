import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
const [directory, output] = process.argv.slice(2);
assert(output, 'Usage: node analyze_locked_toe_deformation.mjs capture-dir report.json');
const read = file => JSON.parse(fs.readFileSync(path.join(directory,file),'utf8'));
const rows=read('frames.json'), graph=read('graph.json'), bindings=read('sole-bindings.json');
const side='Right', foot=graph.names.findIndex(n=>n.toLowerCase()==='foot_r'), toe=graph.names.findIndex(n=>n.toLowerCase()==='ball_r');
assert(foot>=0 && toe>=0 && graph.skeleton.parents[toe]===foot);
const add=(a,b)=>a.map((x,i)=>x+b[i]), sub=(a,b)=>a.map((x,i)=>x-b[i]), scale=(a,s)=>a.map(x=>x*s);
const mul=(a,b)=>[a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1], a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],
  a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3], a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]];
const norm=q=>scale(q,1/Math.hypot(...q)), inv=q=>[-q[0],-q[1],-q[2],q[3]];
const rotate=(q,p)=>mul(mul(q,[...p,0]),inv(q)).slice(0,3);
const transform=(t,p)=>add(t.position,rotate(norm(t.rotation),p.map((x,i)=>x*t.scale[i])));
const compose=(a,b)=>({position:transform(b,a.position),rotation:norm(mul(b.rotation,a.rotation)),scale:a.scale.map((x,i)=>x*b.scale[i])});
const relative=(a,b)=>({position:rotate(inv(norm(b.rotation)),sub(a.position,b.position)).map((x,i)=>x/b.scale[i]),
  rotation:norm(mul(inv(norm(b.rotation)),a.rotation)),scale:a.scale.map((x,i)=>x/b.scale[i])});
const vec=p=>[p.X,p.Y,p.Z];
const points=bindings.points.filter(p=>!p.left).map(p=>({...p,influences:p.influences.map(i=>({...i,
  boneIndex:graph.names.findIndex(n=>n.toLowerCase()===i.bone.toLowerCase())}))}));
assert.equal(points.length,780); assert(points.every(p=>p.influences.every(i=>i.boneIndex>=0)));
const worst=rows.reduce((a,b)=>b.Presentation.IsVisible && b.RefactoredLocks[side].Amount===1 &&
  b.Sole.FullSupport.RightMinimumHeight<a.Sole.FullSupport.RightMinimumHeight?b:a,rows[1]);
const end=worst.Frame-1;
let start=end;
while(start>0 && !rows[start].RefactoredRig.RightCalibrated) {
  assert.equal(rows[start].RefactoredLocks.Right.Amount,1); start--;
}
assert(rows[start].RefactoredRig.RightCalibrated);
const initial=graph.traces[0].frames[start].postRigComponents;
const local=relative(initial[toe],initial[foot]);
const report={directory,startFrame:start+1,worstFrame:end+1,frames:end-start+1,
  maximumSkinErrorCm:0,actualMinimumCm:Infinity,fixedToeMinimumCm:Infinity,worstPoint:null,
  scope:'Offline counterfactual: only ball_r local pose retained from initial full-contact calibration; all other bones and captured world transforms unchanged'};
for(let f=start;f<=end;f++) {
  const g=graph.traces[0].frames[f], poses=g.postRigComponents;
  const w=g.rigInput.ToWorld, world={position:vec(w.Position),rotation:[w.Rotation.X,w.Rotation.Y,w.Rotation.Z,w.Rotation.W],scale:vec(w.Scale)};
  const alternative=poses.slice(); alternative[toe]=compose(local,poses[foot]);
  function minimum(sample) {
    let min=Infinity, point=null;
    for(const p of points) {
      let position=[0,0,0];
      for(const i of p.influences) position=add(position,scale(transform(sample[i.boneIndex],i.position),i.weight));
      const height=transform(world,position)[2];
      if(height<min){min=height;point=p;}
    }
    return {height:min,point};
  }
  const actual=minimum(poses), fixed=minimum(alternative);
  report.maximumSkinErrorCm=Math.max(report.maximumSkinErrorCm,Math.abs(actual.height-rows[f].Sole.FullSupport.RightMinimumHeight*100));
  report.actualMinimumCm=Math.min(report.actualMinimumCm,actual.height);
  report.fixedToeMinimumCm=Math.min(report.fixedToeMinimumCm,fixed.height);
  if(f===end)report.worstPoint=actual.point;
}
assert(report.maximumSkinErrorCm<.01);
fs.writeFileSync(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report));

// Validate the subset actually submitted to the shared Godot tick transaction.
// Native cached weights can outlive a visit, so this is not a proof that every
// native node was visited (or that unbound/evaluator nodes are equivalent).
import fs from 'node:fs';
import assert from 'node:assert/strict';
const [godotPath,nativePath,inventoryPath,reportPath]=process.argv.slice(2);
assert(reportPath,'Usage: node compare-full-graph-ticked-sources.mjs godot.json ue.json inventory.json report.json');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const a=read(godotPath),b=read(nativePath),inventory=read(inventoryPath);
assert.equal(b.source,inventory.source);
// Both exporter inventories enumerate the generated class's reflected struct
// properties. Native players retain that property order, not graph path order.
const nodes=inventory.compiledNodeInventory.filter(n=>n.assetPlayer).sort((x,y)=>x.propertyIndex-y.propertyIndex);
const byId=new Map(nodes.map((n,i)=>[n.compiledNodeIndex,i]));
assert.equal(byId.size,nodes.length);
assert.equal(a.traces.length,b.traces.length);
const report={schemaVersion:1,godotPath,nativePath,inventoryPath,
  scope:'Godot ticked source time and cached weight only; not complete native visitation or pose equivalence',
  tolerance:1e-6,frames:0,ticks:0,failures:0,maxTimeError:0,maxWeightError:0,firstFailures:[]};
const rotates=new Map(nodes.filter(n=>n.asset?.includes('_Rotate_')).map(n=>[n.compiledNodeIndex,
  {compiledIndex:n.compiledNodeIndex,asset:n.asset,ticks:0,firstFrame:null,lastFrame:null}]));
for(let t=0;t<a.traces.length;t++){
  const x=a.traces[t],y=b.traces[t];
  assert.equal(x.name,y.name);assert.equal(x.frames.length,y.frames.length);
  for(let f=0;f<x.frames.length;f++){
    const g=x.frames[f],u=y.frames[f];
    assert.equal(g.serial,u.serial);assert.equal(u.players.length,nodes.length);
    assert.equal(new Set(g.players.map(p=>p.player)).size,g.players.length);
    for(let n=0;n<nodes.length;n++)
      assert.equal(u.players[n].type,nodes[n].class.replace('AnimGraphNode_','AnimNode_'));
    for(const p of g.players){
      assert.equal(typeof p.ticked,'boolean','Capture must include formal IDs and explicit tick membership');
      if(!p.ticked)continue;
      assert(byId.has(p.compiledIndex),`Missing native source ${p.compiledIndex}`);
      const q=u.players[byId.get(p.compiledIndex)];
      const rotation=rotates.get(p.compiledIndex);
      if(rotation){rotation.ticks++;rotation.firstFrame??=g.serial;rotation.lastFrame=g.serial;}
      const timeError=Math.abs(p.time-q.time),weightError=Math.abs(p.weight-q.weight);
      assert(Number.isFinite(timeError)&&Number.isFinite(weightError));
      report.ticks++;
      report.maxTimeError=Math.max(report.maxTimeError,timeError);
      report.maxWeightError=Math.max(report.maxWeightError,weightError);
      if(timeError>report.tolerance||weightError>report.tolerance){
        report.failures++;
        if(report.firstFailures.length<12)report.firstFailures.push({trace:x.name,serial:g.serial,
          compiledIndex:p.compiledIndex,timeError,weightError,godot:p,native:q});
      }
    }
    report.frames++;
  }
}
report.rotateSources=[...rotates.values()];
fs.writeFileSync(reportPath,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({...report,firstFailures:report.firstFailures.map(f=>({trace:f.trace,serial:f.serial,
  compiledIndex:f.compiledIndex,timeError:f.timeError,weightError:f.weightError}))},null,2));
process.exitCode=report.failures?1:0;

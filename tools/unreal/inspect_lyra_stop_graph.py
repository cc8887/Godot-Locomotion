"""Read current compiled Stop closures and verify packages without saving assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root=Path(os.environ['LYRA_OUTPUT_ROOT'])
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
nodes=json.loads((root/'source_nodes.json').read_bytes())
packages=nodes['assetSha256'];content=Path(unreal.Paths.project_content_dir())
def protect():
    for p,h in packages.items():
        if sha(content/(p.split('.')[0].removeprefix('/Game/')+'.uasset'))!=h:
            raise ValueError('Changed Stop graph package: '+p)
protect()
graphs={}
for profile,c in nodes['classes'].items():
    if profile=='main':continue
    cls=unreal.load_class(None,c['class'])
    text=unreal.AlsLyraGraphLibrary.read_animation_layer_graph(cls,'FullBody_StopState')
    if not text:raise ValueError('Missing Stop graph: '+profile)
    graphs[profile]=json.loads(text)
protect()
result={'schemaVersion':1,'sourceNodesSha256':sha(root/'source_nodes.json'),
        'assetSha256':packages,'graphs':graphs}
path=root/'stop_layer_graph.json'
if path.exists():
    if json.loads(path.read_bytes())!=result:raise ValueError('Changed immutable Stop closure')
else:path.write_text(json.dumps(result,separators=(',',':')),encoding='utf-8')
unreal.log('LYRA_STOP_GRAPH_OK profiles='+str(len(graphs))+' packages='+str(len(packages))+' assets_saved=0')

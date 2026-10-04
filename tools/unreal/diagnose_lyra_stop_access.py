"""Bounded original property-access diagnosis; saves no UE assets."""
import json
from pathlib import Path
import unreal
root=Path('.')
requests=json.loads((root/'assets/generated/lyra_als/stop_source_requests.json').read_bytes())
requests['traces']=requests['traces'][:1]
requests['traces'][0]['frames']=requests['traces'][0]['frames'][:10]
nodes=json.loads((root/'assets/generated/lyra_als/source_nodes.json').read_bytes())
text=unreal.AlsLyraGraphLibrary.read_stop_source_trace(unreal.load_class(None,nodes['classes']['main']['class']),
    unreal.load_asset('/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny'),json.dumps(requests))
if not text: raise ValueError('Empty Stop diagnosis')
(root/'artifacts/lyra-analysis/stop-access-diagnostic.json').write_text(text,encoding='utf-8')
unreal.log('LYRA_STOP_ACCESS_DIAGNOSTIC_OK')

"""Read Main's actual shared Lean asset and additive dependencies without saving assets."""
import hashlib
import json
import os
from pathlib import Path
import unreal

root = Path(os.environ['LYRA_OUTPUT_ROOT'])
nodes_bytes = (root / 'source_nodes.json').read_bytes()
nodes = json.loads(nodes_bytes)['classes']['main']['sources']
path = nodes[0]['asset']
if len(nodes) != 3 or any(node['asset'] != path for node in nodes):
    raise ValueError('Changed Main Lean source closure')
space = unreal.load_asset(path)
content = Path(unreal.Paths.project_content_dir())
sha = lambda data: hashlib.sha256(data).hexdigest()
package_sha = lambda asset: sha((content / (asset.split('.')[0].removeprefix('/Game/') + '.uasset')).read_bytes())
assets = {path: package_sha(path)}
rows = []
for index, sample in enumerate(space.get_editor_property('sample_data')):
    sequence = sample.get_editor_property('animation')
    point = sample.get_editor_property('sample_value')
    metadata = json.loads(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(sequence))
    assets[sequence.get_path_name()] = package_sha(sequence.get_path_name())
    if metadata['baseAsset']:
        assets[metadata['baseAsset']] = package_sha(metadata['baseAsset'])
    rows.append({'index': index, 'source': sequence.get_path_name(), 'position': [point.x, point.y, point.z],
                 'rateScale': sample.get_editor_property('rate_scale'),
                 'singleFrame': sample.get_editor_property('use_single_frame_for_blending'),
                 'frameIndex': sample.get_editor_property('frame_index_to_sample'),
                 'mirror': sample.get_editor_property('mirror'), 'metadata': metadata})
axes = [{'name': str(p.get_editor_property('display_name')), 'min': p.get_editor_property('min'),
         'max': p.get_editor_property('max'), 'grid': p.get_editor_property('grid_num'),
         'wrap': p.get_editor_property('wrap_input')} for p in space.get_editor_property('blend_parameters')]
data = {'schemaVersion': 1, 'sourceNodesSha256': sha(nodes_bytes), 'source': path, 'axes': axes, 'samples': rows,
        'nativeTriangulation': json.loads(unreal.AlsSourceAnimationLibrary.read_blend_space_triangulation_reference(space, '{"inputs":[[0,0]]}')),
        'useGrid': space.get_editor_property('interpolate_using_grid'),
        'weightSpeed': space.get_editor_property('target_weight_interpolation_speed_per_sec'),
        'axisToScale': str(space.get_editor_property('axis_to_scale_animation')),
        'assetSha256': assets}
destination = root / 'main_lean'; destination.mkdir(exist_ok=True)
output = destination / 'inventory.json'
if output.exists():
    if json.loads(output.read_bytes()) != data:
        raise ValueError('Existing Main Lean inventory differs')
else:
    output.write_text(json.dumps(data, separators=(',', ':')), encoding='utf-8')
if any(package_sha(asset) != digest for asset, digest in assets.items()):
    raise ValueError('Main Lean inspection changed a source package')
unreal.log('LYRA_MAIN_LEAN_INVENTORY_OK sources=3 samples=3 assets_saved=0')

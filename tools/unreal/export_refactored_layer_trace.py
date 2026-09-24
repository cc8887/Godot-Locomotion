"""Run the original Layering graph with controlled inputs; no asset modification."""
import hashlib
import json
import os
from pathlib import Path
import re
import unreal

repository = Path(__file__).parents[2]
output = Path(os.environ["ALS_LAYER_TRACE_OUTPUT"])
if not output.is_absolute():
    raise ValueError("Absolute output required")
graphs = (repository / "assets/config/refactored_layering_graphs.json").read_bytes()
text = json.loads(graphs)["blueprints"][2]["nativeText"]
fields = sorted(set(re.findall(r'PropertyPath=\("GetParent","LayeringState","(\w+)"\)', text)))
if len(fields) != 22:
    raise ValueError("Changed parent state closure")
traces = []
for hz in (30, 60, 120):
    frames = []
    previous = (0, 0)
    for frame in range(hz):
        phase = frame * 6 // hz
        values = (0, .25, .75, 1, -.2, 1.2)
        layering = {name: values[(phase + index) % len(values)] for index, name in enumerate(fields)}
        for arm in ("ArmLeft", "ArmRight"):
            layering[arm + "LocalSpaceBlendAmount"] = phase % 2
            layering[arm + "MeshSpaceBlendAmount"] = 1 - phase % 2
        stance = ((1, 0), (.3, .7), (0, 1), (0, 0), (-.2, 1.2), (1, 0))[phase]
        frames.append({"delta": 1 / hz, "layering": layering,
                       "stance": {"StandingAmount": previous[0], "CrouchingAmount": previous[1]},
                       "locomotionStanding": phase % 2 == 0, "overlayStanding": phase % 2 != 0,
                       "locomotionCurves": {"PoseStanding": stance[0], "PoseCrouching": stance[1], "OnlyLocomotion": .4, "LayerHead": -.1},
                       "overlayCurves": {"LayerHead": 1, "LayerHeadSlot": .8, "OnlyOverlay": .3}})
        previous = stance
    traces.append({"name": str(hz) + "hz", "frames": frames})
request = {"schemaVersion": 1, "graphsSha256": hashlib.sha256(graphs).hexdigest(),
           "baseInputsSha256": hashlib.sha256((repository / "assets/config/refactored_base_pose_inputs.json").read_bytes()).hexdigest(),
           "inventorySha256": hashlib.sha256((repository / "assets/config/refactored_layering_inventory.json").read_bytes()).hexdigest(),
           "traces": traces}
request["requestDigest"] = hashlib.sha256(json.dumps(request, separators=(",", ":"), allow_nan=False).encode()).hexdigest()
request_path = output.with_suffix(".request.json")
output.parent.mkdir(parents=True, exist_ok=True)
request_path.write_text(json.dumps(request, separators=(",", ":"), allow_nan=False) + "\n", encoding="utf-8", newline="\n")
if not unreal.AlsAnimationGraphLibrary.export_refactored_layer_graph_trace(str(request_path), str(output)):
    raise RuntimeError("Native Layering graph export failed")
if os.environ.get("ALS_LAYER_TRACE_QUIT") == "1":
    ticks = 0
    def finish(delta):
        global ticks
        ticks += 1
        if ticks >= 3:
            unreal.unregister_slate_post_tick_callback(handle)
            unreal.SystemLibrary.quit_editor()
    handle = unreal.register_slate_post_tick_callback(finish)

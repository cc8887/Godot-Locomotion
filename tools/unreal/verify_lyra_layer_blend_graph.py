"""Compare the current Lyra layer weight graphs with the retained source snapshot."""

import hashlib
from pathlib import Path

import unreal


asset = "/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase"
snapshot = (
    Path(unreal.Paths.project_saved_dir())
    / "BP2DSL/Exports/20260913-134530-535215/Lyra/BlueprintLisp/Game"
    / "Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase/Function"
)
expected_hashes = {
    "Update Blend Weight Data":
        "c1ad3ddde48cbfc20a637ae4bbea22773b84724d8bbe62031026d48a97000019",
    "BlueprintThreadSafeUpdateAnimation":
        "aa9611bfad0315cc52ec49465a6251da08c24b885ef5b9918af2d185831d4de2",
}
for graph, expected_hash in expected_hashes.items():
    result = unreal.AnimBP2FPPythonBridge.export_event_graph_to_text(
        asset, graph, False, True
    )
    if not result.success:
        raise RuntimeError(f"Could not export current {graph}: {result.message}")
    current = str(result.dsl_text).replace("\r\n", "\n").strip()
    archived = (snapshot / f"{graph}.bplisp").read_text(encoding="utf-8")
    if current != archived.replace("\r\n", "\n").strip():
        raise RuntimeError(f"Current Lyra graph differs from 2026-09-13 snapshot: {graph}")
    digest = hashlib.sha256(current.encode("utf-8")).hexdigest()
    if digest != expected_hash:
        raise RuntimeError(f"Lyra source graph hash changed: {graph}: {digest}")
    unreal.log(f"LYRA_LAYER_GRAPH_OK graph={graph} sha256={digest}")

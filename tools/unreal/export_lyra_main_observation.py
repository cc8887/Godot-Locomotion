"""Real Character PropertyAccess -> ordered original Main observation functions; no asset saves."""
import hashlib
import json
import math
import os
from pathlib import Path
import unreal

root = Path(os.environ["LYRA_OUTPUT_ROOT"])
sha = lambda data: hashlib.sha256(data).hexdigest()
names = ("main_lean/catalog.json", "main_lean/inventory.json", "source_nodes.json")
files = {name: (root / name).read_bytes() for name in names}
assets = {}
for data in files.values():
    for path, value in json.loads(data)["assetSha256"].items():
        if path in assets and assets[path] != value:
            raise ValueError("Conflicting package provenance")
        assets[path] = value
content = Path(unreal.Paths.project_content_dir())
def package_hash(path):
    return sha((content / (path.split(".")[0].removeprefix("/Game/") + ".uasset")).read_bytes())
def protect():
    if any(package_hash(path) != value for path, value in assets.items()):
        raise ValueError("Protected package changed")
def save(name, data):
    path = root / name
    if path.exists():
        if json.loads(path.read_bytes()) != data:
            raise ValueError("Existing immutable observation fixture differs: " + name)
    else:
        path.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")
    return sha(path.read_bytes())
protect()
traces = []
for hz in (30, 60, 120):
    frames = []
    position = [1337.125, -781.75, 111.5]
    for index in range(hz * 12):
        t = index / hz
        delta = 0 if index in (hz * 4, hz * 8) else 1e-6 if index == hz * 6 else 1 / hz
        cell = int(t * 4)
        direction = (0, 50, 68, 110, 130, 175, -170, -130, -110, -68, -50, 0)[cell % 12]
        speed = (0, 1e-5, .0005, .001, .01, 50, 199.999, 200, 400)[cell % 9]
        velocity = [math.cos(math.radians(direction)) * speed, math.sin(math.radians(direction)) * speed,
                    350 if 3 <= t < 3.5 else -100 if 3.5 <= t < 4 else 0]
        acceleration_angle = direction + (0, 90, 180, -90)[cell % 4]
        acceleration_size = (0, .0005, .001, .009999, .01, .1, .100001, 2048)[cell % 8]
        acceleration = [math.cos(math.radians(acceleration_angle)) * acceleration_size,
                        math.sin(math.radians(acceleration_angle)) * acceleration_size, 77.25]
        if 10 <= t < 10.5:
            velocity = [100, 0, 0]
            acceleration = [-100, 0, 0]  # opposing recursive pivot cancels, then resumes
        for axis in range(3):
            position[axis] += velocity[axis] / hz
        if index == hz * 5:
            position[0] += 10000.125  # teleport; original has no displacement clamp
        frames.append({"delta": delta, "location": position.copy(),
            "rotation": [13.25 * math.sin(t * .73), 173 + t * 35.123456789,
                         -7.75 * math.cos(t * .47)],
            "velocity": velocity, "acceleration": acceleration,
            "movementMode": 3 if 3 <= t < 4 or 9 <= t < 9.5 else 1,
            "crouching": 2 <= t < 5 or t >= 11, "ads": 4 <= t < 8,
            "firing": index % 19 == 0, "first": index < 2 or index == 7 * hz,
            "rootYaw": 60 * math.sin(t * .91)})
    traces.append({"hz": hz, "frames": frames})
requests = {"schemaVersion": 1, "traces": traces}
main_path = json.loads(files["source_nodes.json"])["classes"]["main"]["class"]
result = unreal.AlsLyraGraphLibrary.read_main_observation_trace(
    unreal.load_class(None, main_path),
    unreal.load_asset("/Game/Characters/Heroes/Mannequin/Meshes/SKM_Manny"),
    json.dumps(requests, separators=(",", ":")))
if not result:
    raise ValueError("Empty native Main observation trace")
native = json.loads(result)
if len(native["traces"]) != 3:
    raise ValueError("Incomplete observation capture")
for trace, capture in zip(traces, native["traces"]):
    if trace["hz"] != capture["hz"] or len(trace["frames"]) != len(capture["frames"]):
        raise ValueError("Wrong native observation identity")
    for frame, row in zip(trace["frames"], capture["frames"]):
        # Preserve the actual actor/property-access INPUT boundary, not expected Main output.
        frame["snapshot"] = row["input"]
protect()
request_sha = save("main_observation_requests.json", requests)
payload = {"schemaVersion": 1, "requestSha256": request_sha,
    "dependencies": {name: sha(value) for name, value in files.items()}, "assetSha256": assets,
    "order": native["order"], "traces": native["traces"],
    "scope": "Real ACharacter/CharacterMovement snapshots and PropertyAccess, original first six Main update functions in order. Explicit external tags/root-yaw/first inputs; not whole BlueprintThreadSafeUpdateAnimation, Godot physics conversion or final pose."}
save("main_observation_native.json", payload)
policy = {"schemaVersion": 1, "dependencies": payload["dependencies"], "order": native["order"],
          "initial": native["traces"][0]["initial"],
          "deadZone": native["traces"][0]["initial"]["CardinalDirectionDeadZone"],
          "scope": payload["scope"]}
if any(trace["initial"] != policy["initial"] for trace in native["traces"]):
    raise ValueError("Main initial state depends on frame rate")
save("main_observation_policy.json", policy)
unreal.log(f"LYRA_MAIN_OBSERVATION_NATIVE_OK traces=3 frames={sum(len(t['frames']) for t in traces)} stages=6 packages={len(assets)} assets_saved=0")

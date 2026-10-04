"""Audit actual process results and the bounds of the physical Warp integration."""
import hashlib
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts/lyra-analysis"
ASSETS = ROOT / "assets/generated/lyra_als"
PROJECT = ROOT.parent / "GASP58"
cache = {}


def sha(path):
    key = str(path)
    if key not in cache:
        cache[key] = hashlib.sha256(path.read_bytes()).hexdigest()
    return cache[key]


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def require(value, message):
    if not value:
        raise RuntimeError(message)


def package_file(path):
    path = path.split(".")[0]
    if path.startswith("/Game/"):
        return PROJECT / "Content" / (path.removeprefix("/Game/") + ".uasset")
    if path.startswith("/ShooterCore/"):
        return PROJECT / "Plugins/GameFeatures/ShooterCore/Content" / (path.removeprefix("/ShooterCore/") + ".uasset")
    raise ValueError(path)


def main():
    final = OUT / "warp-physical-verification.json"
    require(not final.exists(), "Preserve final physical Warp evidence")
    rows = []
    for configuration in ("debug", "optimize"):
        log = (OUT / f"warp-physical-{configuration}-final.log").read_text(encoding="utf-8-sig")
        require(not re.search(r"^\s*(?:ERROR|WARNING):", log, re.M), "Godot issue in physical matrix")
        exits = re.findall(r"LYRA_WARP_PHYSICAL_RUN_EXIT name=(\S+) code=(\d+)", log)
        require(len(exits) == 7 and all(code == "0" for _, code in exits), "Actual seven-process matrix not complete")
        require(f"LYRA_WARP_PHYSICAL_MATRIX_OK configuration={configuration.title() if configuration == 'debug' else 'Optimize'}" in log, "Matrix did not finish")
        require("traces=120 frames=16800 nonzero=3030" in log and "bankContextFrames=15120" in log, "Original component/live bank context missing")
        require("LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK cases=5" in log and "LYRA_ROOT_MOVEMENT_LATE_FRAME_OK" in log, "Prior physical root boundary missing")
        require("LYRA_WEAPON_EQUIPMENT_NATIVE_GODOT_OK" in log and "LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6" in log, "Equipment regression missing")
        loaded = dict(re.findall(r"LYRA_WARP_PHYSICAL_ASSEMBLY file=(\S+) sha256=(\w+)", log))
        assembly_dir = ROOT / ".godot/mono/temp/bin" / ("Debug" if configuration == "debug" else "ExportRelease")
        require(len(loaded) == 3, "Actual loaded assemblies not recorded")
        for name, value in loaded.items():
            require(sha(assembly_dir / name).upper() == value, "Loaded assembly differs from verified final build")
        for hz in (30, 60, 120):
            row = read(OUT / f"warp-physical-{configuration}-{hz}-final.json")
            require(row["hz"] == hz and row["roles"] == 6 and row["frames"] == hz * 8, "Physics matrix scope differs")
            require(row["moves"] == hz * 48 == row["preRetries"] == row["animationRetries"], "Single physical publication/retry incomplete")
            require(all(row[k] > 0 for k in ("nonzero", "blocked", "disabled", "paused")), "Warp physical behavior uncovered")
            require(row["changed"] == row["destroyed"] == 4 and row["reached"] == 8, "Original four-window target checks missing")
            require(row["actualJolt"] and row["actualAlsComponent"] and row["explicitComponent"], "Synthetic scene substituted for live character")
            require(not row["originalAbilityExecuted"] and not row["wholeWorldNative"], "Verification scope overstated")
            other = read(OUT / f"warp-physical-debug-{hz}-final.json")
            require(row == other, "Debug/Optimize physical histories differ")
            rows.append(dict(configuration=configuration, **row))
        ordinary = read(OUT / f"warp-physical-{configuration}-main-final.json")
        require(ordinary == read(OUT / "root-movement-debug-main-final6.json"), "Default Shooter behavior changed")
        require(not ordinary["player"]["model"]["motionWarpingConsumer"], "Default Shooter invented a MotionWarping component")
    optimize_log = (OUT / "warp-physical-optimize-final.log").read_text(encoding="utf-8-sig")
    require("LYRA_WARP_PHYSICAL_DEBUG_RESTORED hashVerified=true" in optimize_log, "Debug restoration not confirmed")
    for p in (OUT / "warp-physical-debug-backup-final").iterdir():
        require(sha(p) == sha(ROOT / ".godot/mono/temp/bin/Debug" / p.name), "Debug file restoration differs")
    for name in ("motion-warping-physical-build-complete.log", "motion-warping-physical-optimize-build-complete.log"):
        text = (OUT / name).read_text(encoding="utf-8-sig")
        require(re.search(r"^\s*0\s*(?:个警告|Warning)", text, re.M) and re.search(r"^\s*0\s*(?:个错误|Error)", text, re.M), "Unclean final build")
    trx = ET.parse(OUT / "warp-physical-core-final.trx").getroot()
    counters = next(n for n in trx.iter() if n.tag.endswith("Counters"))
    require(counters.attrib["total"] == counters.attrib["passed"] == "193" and counters.attrib["failed"] == "0", "Core regressions did not pass")
    protected = {}
    packages = {}
    for name in ("motion_warping_v1_policy.json", "motion_warping_v1_usage.json", "motion_warping_v1_game_feature.json"):
        d = read(ASSETS / name)
        for relative, value in d["previousFixtureSha256"].items():
            require(sha(ASSETS / relative) == value, f"Existing JSON changed: {relative}")
            protected[relative] = value
        for relative, value in d["protectedProject"].items():
            require(sha(PROJECT / relative) == value, "Original project/configuration changed")
        if isinstance(d["assetSha256"], dict):
            packages.update(d["assetSha256"])
        else:
            packages[d["path"]] = d["assetSha256"]
    for path, value in packages.items():
        require(sha(package_file(path)) == value, f"Original asset changed: {path}")
    require(len(protected) == 851 and len(list(ASSETS.rglob("*.json"))) == 852, "Original resource closure changed")
    native = read(ASSETS / "motion_warping_v1_native.json")
    require(native["usageSha256"] == sha(ASSETS / "motion_warping_v1_usage.json") and native["requestSha256"] == sha(ASSETS / "motion_warping_v1_requests.json"), "Original native reference changed")
    require(not (ROOT.parent / "GASP58/Plugins/LyraMotionWarpingOracle").exists(), "Temporary native probe left installed")
    render_log = (OUT / "warp-physical-render-final.stdout.log").read_text(encoding="utf-8-sig")
    render_err = (OUT / "warp-physical-render-final.stderr.log").read_text(encoding="utf-8-sig")
    require("LYRA_MOTION_WARPING_PHYSICS_GODOT_OK" in render_log and not re.search(r"^\s*(?:ERROR|WARNING):", render_log + render_err, re.M), "Actual rendered character run failed")
    render_exit = read(OUT / "warp-physical-render-final-process.json")
    require(render_exit["exitCode"] == 0, "Render process did not terminate successfully")
    require(read(OUT / "warp-physical-render-final.json") == read(OUT / "warp-physical-debug-60-final.json"), "Render physical history differs")
    captures = [OUT / f"warp-physics-render-{frame}.png" for frame in (30, 90, 270)]
    require(all(p.stat().st_size > 10000 for p in captures), "Rendered captures missing")
    result = dict(
        scope=dict(originalFourTemplates=True, liveMontageContextFrames=15120, recordedContextForSeek=True,
                   actualCapsuleIntegrated=True, optInComponent=True, defaultShooterUnchanged=True,
                   originalAbilityExecuted=False, wholeUEWorldPhysics=False, wholeMainNative=False, goalComplete=False),
        runs=rows, corePassed=193, protectedJson=len(protected), protectedPackages=len(packages), actualGodotProcesses=15,
        originalNativeFramesPerBuild=16800, originalNativeNonzeroPerBuild=3030,
        renderedFrames=480, captures=[dict(path=str(p.relative_to(ROOT)), sha256=sha(p)) for p in captures],
    )
    final.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["scope"]))
    print("LYRA_WARP_PHYSICAL_VERIFIED processes=15 builds=2 core=193")


if __name__ == "__main__":
    main()

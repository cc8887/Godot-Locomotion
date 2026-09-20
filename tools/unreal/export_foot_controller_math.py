"""Read-only AnimationCore stretch / native rotator oracle; no asset writes."""
import json
import os
import struct
from pathlib import Path
import unreal


def export():
    output = Path(os.environ["ALS_FOOT_CONTROLLER_MATH_OUTPUT"])
    if not output.is_absolute():
        raise ValueError("ALS_FOOT_CONTROLLER_MATH_OUTPUT must be absolute")
    animation = unreal.get_default_object(unreal.load_class(None, "/Script/AnimGraphRuntime.KismetAnimationLibrary"))
    math = unreal.get_default_object(unreal.MathLibrary)
    def vv(v):
        return [v.x, v.y, v.z]
    def f32(x):
        return struct.unpack("f", struct.pack("f", x))[0]
    rows = []
    for root, joint, end, pole in (
        ((0, 0, 0), (40, 0, 0), (100, 0, 0), (0, 100, 0)),
        ((17, -8, 30), (37, 12, 0), (17, 22, -40), (80, 100, 50)),
        ((0, 0, 0), (0, 0, 40), (0, 0, 100), (0, 0, 70)),
    ):
        for target in ((0, 0, 0), (100, 0, 0), (100.005, 0, 0), (120, 0, 0), (150, 0, 0),
                       (170, 0, 0), (-120, 30, 20), (0, 0, 85)):
            for allow, start, maximum in ((False, 1, 1.5), (True, 1, 1.5), (True, .8, 1.5), (True, 1.5, 1.5)):
                start, maximum = f32(start), f32(maximum)
                vectors = [unreal.Vector(*v) for v in (root, joint, end, pole, target)]
                result = animation.call_method("K2_TwoBoneIK", args=tuple(vectors) + (allow, start, maximum))
                rows.append(dict(zip(("root", "joint", "end", "pole", "target"), map(vv, vectors)),
                                 allow=allow, start=start, maximum=maximum, resultJoint=vv(result[0]), resultEnd=vv(result[1])))
    rotations = []
    for p, y, r in ((0, 0, 0), (90, 0, 0), (0, 90, 0), (0, 0, 90), (-25, 163.123456, 11),
                    (14, 179, -7), (725, -450, 1087)):
        rotation = unreal.Rotator(pitch=p, yaw=y, roll=r)
        q = math.call_method("Conv_RotatorToQuaternion", args=(rotation,))
        rotations.append({"rotation": [rotation.pitch, rotation.yaw, rotation.roll], "quaternion": [q.x, q.y, q.z, q.w]})
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"schemaVersion": 1, "source": "UE5.9 AnimationCore::SolveTwoBoneIK and FRotator::Quaternion",
                                  "rows": rows, "rotations": rotations}, indent=2) + "\n", encoding="utf-8")
    unreal.log(f"ALS_FOOT_CONTROLLER_MATH_OK rows={len(rows)} rotations={len(rotations)} assets_saved=0")


export()

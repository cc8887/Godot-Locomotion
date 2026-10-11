import json
import os

import unreal

# 单条试跑: AS_ALS_N_Walk_F 普通版 -> ALS_Final
report = {}

def die(msg):
    print("KIMODO_SMOKE_FAIL " + msg)
    raise RuntimeError(msg)

fbx_out = "F:/Github/Godot-Locomotion/assets/generated/kimodo_ue/als_regen/fbx_als/ALS_N_Walk_F.fbx"
in_path = "/Game/KimodoMocara/ALS_Regen/AS_ALS_N_Walk_F"

if not os.path.exists(in_path):
    pass

os.environ["KIMODO_INPUT_ASSET_PATH"] = in_path
os.environ["KIMODO_ASSET_PATH"] = "/Game/KimodoMocara/ALS_Final/AS_ALS_N_Walk_F"
os.environ["KIMODO_FBX_OUT"] = fbx_out
os.environ.pop("KIMODO_SEARCH", None)
os.environ.pop("KIMODO_REPLACE", None)

try:
    import runpy
    runpy.run_path("F:/Github/Godot-Locomotion/tools/unreal/retarget_als_final.py", run_name="__main__")
    print("KIMODO_SMOKE_DONE walk_f")
except Exception as e:
    import traceback
    traceback.print_exc()
    die("smoke failed: " + repr(e))

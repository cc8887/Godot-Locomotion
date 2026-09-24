"""Export the complete authored mantle animation closure, separate from evaluated poses.

Uses the six frozen Refactored montage bindings. No asset saves or FBX conversion.
"""
import hashlib
import json
import os
import runpy
import tempfile
from pathlib import Path
import unreal

output=Path(os.environ["ALS_MANTLE_ANIMATION_OUTPUT"])
oracle=Path(os.environ["ALS_MANTLE_POSE_OUTPUT"])
if not output.is_absolute() or not oracle.is_absolute() or output==oracle:
    raise ValueError("Distinct absolute source and oracle paths required")
repository=Path(__file__).parents[2]
bindings=json.loads((repository/"assets/config/refactored_mantle_root_tracks.json").read_text(encoding="utf-8"))
read_asset=runpy.run_path(str(Path(__file__).with_name("export_action_notify_inputs.py")))["read_asset"]
sequences,skeletons,montages,poses=[],{},[],[]

def decode(text):
    return json.loads(text,parse_int=lambda token: -0.0 if token=="-0" else int(token))

with tempfile.TemporaryDirectory(prefix="als-mantle-animation-") as temporary:
    def native_text(asset):
        task=unreal.AssetExportTask()
        task.object=asset
        task.exporter=unreal.ObjectExporterT3D()
        task.filename=str(Path(temporary)/"object.t3d")
        task.automated,task.prompt=True,False
        if not unreal.Exporter.run_asset_export_task(task):
            raise RuntimeError("Native text export failed: "+asset.get_path_name())
        data=Path(task.filename).read_bytes()
        return data.decode("utf-16" if data.startswith((b"\xff\xfe",b"\xfe\xff")) else "utf-8-sig")

    for binding in bindings["sequences"]:
        path=binding["source"]
        asset=unreal.load_asset(path)
        if not isinstance(asset,unreal.AnimSequence):
            raise RuntimeError("Missing exact mantle sequence: "+path)
        raw=decode(unreal.AlsSourcePoseKeysLibrary.read_source_pose_keys(asset))
        skeleton=asset.get_editor_property("skeleton")
        skeleton_path=skeleton.get_path_name()
        if skeleton_path not in skeletons:
            skeletons[skeleton_path]={"metadata":decode(unreal.AlsSourceAnimationLibrary.read_skeleton_pose_metadata(skeleton)),
                                      "nativeText":native_text(skeleton)}
        if raw["skeletonSource"]!=binding["skeletonSource"] or raw["sampledKeyCount"]!=binding["sampledKeyCount"]:
            raise RuntimeError("Mantle root/pose source timing or skeleton changed")
        roots=[t for t in raw["tracks"] if t["bone"].casefold()==binding["rootName"].casefold()]
        if roots!=binding["tracks"]:
            raise RuntimeError("Existing root channels changed")
        sequences.append({"raw":raw,"evaluation":decode(unreal.AlsSourceAnimationLibrary.read_source_animation_metadata(asset)),
                          "notifies":read_asset(path)["notifies"],"nativeText":native_text(asset)})
        samples=[]
        for fraction in (0,.137,.419,.773,1):
            time=raw["playLength"]*fraction
            for label,retarget,extract,ignore_lock in (("raw",False,False,True),("retargeted",True,False,True),
                                                       ("asset_root_lock",True,False,False),("extract_root_lock",True,True,False)):
                sample=decode(unreal.AlsSourceAnimationLibrary.read_raw_bone_pose(asset,time,retarget,extract,ignore_lock))
                sample.update(context=label,requestedTime=time)
                samples.append(sample)
        poses.append({"source":path,"samples":samples})
    for binding in bindings["montages"]:
        asset=unreal.load_asset(binding["path"])
        slots=asset.get_editor_property("slot_anim_tracks")
        if len(slots)!=1 or len(slots[0].get_editor_property("anim_track").get_editor_property("anim_segments"))!=1:
            raise RuntimeError("Changed mantle montage topology")
        if asset.get_first_anim_reference().get_path_name()!=binding["segments"][0]["sequence"]:
            raise RuntimeError("Changed mantle sequence binding")
        blend_in,blend_out=asset.get_blend_in_args(),asset.get_blend_out_args()
        def blend(args):
            curve=args.get_editor_property("custom_curve")
            return {"time":args.get_editor_property("blend_time"),"option":args.get_editor_property("blend_option").name,
                    "customCurve":curve.get_path_name() if curve else None}
        row=read_asset(binding["path"])
        for notify in row["notifies"]:
            state=unreal.load_object(None,notify["stateObject"]) if notify["stateObject"] else None
            if state is None:
                raise RuntimeError("Unexpected mantle montage notify kind")
            def tag(name):
                return str(state.get_editor_property(name).get_editor_property("tag_name"))
            if notify["class"]=="/Script/ALS.AlsAnimNotifyState_EarlyBlendOut":
                notify["payload"]={key:state.get_editor_property(key) for key in (
                    "blend_out_duration","check_input","check_locomotion_mode","check_rotation_mode","check_stance")}
                notify["payload"].update({key:tag(key) for key in ("locomotion_mode_equals","rotation_mode_equals","stance_equals")})
            elif notify["class"]=="/Script/ALS.AlsAnimNotifyState_SetLocomotionAction":
                notify["payload"]={"locomotion_action":tag("locomotion_action")}
            else:
                raise RuntimeError("Unrecognized mantle montage notify state: "+notify["class"])
        row.update(nativeText=native_text(asset),skeleton=asset.get_editor_property("skeleton").get_path_name(),
                   slot=str(slots[0].get_editor_property("slot_name")),segments=binding["segments"],
                   length=asset.get_play_length(),rateScale=asset.get_editor_property("rate_scale"),
                   blendIn=blend(blend_in),blendOut=blend(blend_out),
                   autoBlendOut=asset.get_editor_property("enable_auto_blend_out"),
                   blendOutTriggerTime=asset.get_editor_property("blend_out_trigger_time"))
        montages.append(row)

def write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(data,separators=(",",":"),allow_nan=False)+"\n",encoding="utf-8",newline="\n")

write(output,{"schemaVersion":1,"rootBindingsSha256":hashlib.sha256((repository/"assets/config/refactored_mantle_root_tracks.json").read_bytes()).hexdigest(),
              "sequences":sequences,"skeletons":skeletons,"montages":montages})
write(oracle,{"schemaVersion":1,"sourceSha256":hashlib.sha256(output.read_bytes()).hexdigest(),"sequences":poses})
unreal.log(f"ALS_MANTLE_ANIMATION_OK sequences={len(sequences)} skeletons={len(skeletons)} montages={len(montages)} poses={sum(len(p['samples']) for p in poses)} assets_saved=0")

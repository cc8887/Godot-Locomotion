using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Runtime;

namespace GodotAls.Animation;

public partial class MantlingVisualSmoke : Node3D
{
    public override async void _Ready()
    {
        try
        {
            Run();
            var capture=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--capture="));
            if(capture is not null)await Capture(capture[10..]);
            GetTree().Quit();
        }
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    private async Task Capture(string directory)
    {
        if(!System.IO.Path.IsPathFullyQualified(directory))throw new ArgumentException("Capture directory must be absolute.");
        System.IO.Directory.CreateDirectory(directory);
        string Read(string name)=>Godot.FileAccess.GetFileAsString("res://assets/config/"+name+".json");
        var source=AlsMantlingPoseCompiler.Compile(Read("refactored_mantle_animation_inputs"),Read("refactored_mantle_root_tracks"))
            .Values.Single(s=>s.Data.Identity.AssetPath.Contains("_High."));
        var set=ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var bindings=new List<AlsMantlingVisualBinding>();
        foreach(var name in new[]{"Mannequin","AnimMan"})
        {
            var asset=set.SkeletalMeshes.Single(m=>m.ObjectPath==AlsPhysicsAssetCompiler.MeshRoot+name+"."+name);
            var model=ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot+"/"+asset.ResourcePath).Instantiate<Node3D>();
            AddChild(model);model.Position=new(bindings.Count==0?-.8f:.8f,0,0);
            bindings.Add(new(AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!,source));
        }
        AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,
            BackgroundColor=new Color(.12f,.14f,.18f),AmbientLightSource=Godot.Environment.AmbientSource.Color,
            AmbientLightColor=Colors.White,AmbientLightEnergy=.7f}});
        var light=new DirectionalLight3D{RotationDegrees=new(-35,-25,0),LightEnergy=1.5f};AddChild(light);
        var floor=new MeshInstance3D{Mesh=new PlaneMesh{Size=new(6,6)}};AddChild(floor);
        var camera=new Camera3D{Position=new(2,2.1f,3.8f),Fov=45,Current=true};AddChild(camera);camera.LookAt(new(0,1,0));
        var sampler=source.CreateSampler();var pose=new AlsPrecisePose[79];
        for(var frame=0;frame<3;frame++)
        {
            sampler.Sample(source.Data.PlayLength*(.2+frame*.3),true,false,false,pose);
            foreach(var binding in bindings)binding.Apply(pose);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var image=GetViewport().GetTexture().GetImage();
            if(image.SavePng(System.IO.Path.Combine(directory,$"mantle-{frame}.png"))!=Error.Ok)throw new Exception("Mantle capture failed.");
        }
        GD.Print("ALS_MANTLING_CAPTURE_OK frames=3 models=2 action=High root_lock=true");
    }
    private void Run()
    {
        string Read(string name)=>Godot.FileAccess.GetFileAsString("res://assets/config/"+name+".json");
        var inputs=Read("refactored_mantle_animation_inputs");
        var sources=AlsMantlingPoseCompiler.Compile(inputs,Read("refactored_mantle_root_tracks"));
        var curves=AlsMantlingCurveCompiler.Compile(Read("refactored_mantle_curves"),inputs);
        var set=ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var count=0;var rejected=0;float maximum=0;
        foreach(var name in new[]{"Mannequin","AnimMan"})
        {
            var asset=set.SkeletalMeshes.Single(m=>m.ObjectPath==AlsPhysicsAssetCompiler.MeshRoot+name+"."+name);
            var model=ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot+"/"+asset.ResourcePath).Instantiate<Node3D>();AddChild(model);
            try
            {
                var skeleton=AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                foreach(var source in sources.Values)
                {
                    var binding=new AlsMantlingVisualBinding(skeleton,source);var sampler=source.CreateSampler(curves[source.Data.Identity.AssetPath]);
                    var pose=new AlsPrecisePose[79];var component=new AlsPrecisePose[79];var values=new AlsInertialCurve[2];
                    for(var frame=0;frame<=30;frame++)
                    {
                        sampler.Sample(source.Data.PlayLength*frame/30,true,false,false,pose,values);binding.Apply(pose);
                        for(var bone=0;bone<pose.Length;bone++)component[bone]=source.Parents[bone]<0?pose[bone]:
                            AlsPrecisePose.Compose(pose[bone],component[source.Parents[bone]]);
                        foreach(var logical in source.Data.PhysicalToLogical)
                        {
                            var actual=skeleton.GetBoneGlobalPose(skeleton.FindBone(source.BoneNames[logical]));
                            var expected=AlsMantlingVisualBinding.ToFbx(component[logical]);
                            var error=actual.Origin.DistanceTo(expected.Origin);maximum=MathF.Max(maximum,error);
                            if(error>.0002f||actual.Basis.Scale.DistanceTo(expected.Basis.Scale)>.0002f||
                                1-MathF.Abs(actual.Basis.GetRotationQuaternion().Dot(expected.Basis.GetRotationQuaternion()))>1e-5f)
                                throw new InvalidOperationException($"Mantle skin mismatch {name} frame={frame} bone={source.BoneNames[logical]} m={error:R}");
                            count++;
                        }
                    }
                    var blocked=Task.Run(()=>{try{binding.Apply(pose);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();
                    if(!blocked)throw new Exception("Worker wrote visual skeleton.");rejected++;
                    var last=source.Data.PhysicalToLogical[^1];var saved=pose[last];var rootBefore=skeleton.GetBonePosePosition(0);var savedRoot=pose[0];
                    pose[0]=savedRoot with{Position=savedRoot.Position+new AlsDoubleVector(20,0,0)};
                    pose[last]=saved with{Position=new(double.NaN,0,0)};
                    try{binding.Apply(pose);throw new Exception("Invalid pose was accepted.");}catch(ArgumentException){rejected++;}
                    if(rootBefore!=skeleton.GetBonePosePosition(0))throw new Exception("Invalid tail partially wrote pose.");pose[last]=saved;pose[0]=savedRoot;
                    var visualLast=skeleton.FindBone(source.BoneNames[last]);var oldName=skeleton.GetBoneName(visualLast);
                    skeleton.SetBoneName(visualLast,"replaced_bone");
                    try{binding.Apply(pose);throw new Exception("Changed skeleton accepted.");}catch(InvalidOperationException){rejected++;}
                    skeleton.SetBoneName(visualLast,oldName);
                    var oldRest=skeleton.GetBoneRest(visualLast);var wrongRest=oldRest;wrongRest.Origin+=Vector3.One;
                    skeleton.SetBoneRest(visualLast,wrongRest);
                    try{_=new AlsMantlingVisualBinding(skeleton,source);throw new Exception("Incompatible rest accepted.");}catch(ArgumentException){rejected++;}
                    skeleton.SetBoneRest(visualLast,oldRest);
                }
            }
            finally{model.Free();}
        }
        GD.Print($"ALS_MANTLING_VISUAL_OK models=2 sources=3 frames=186 bones={count} rejected={rejected} max_m={maximum:R}");
    }
}

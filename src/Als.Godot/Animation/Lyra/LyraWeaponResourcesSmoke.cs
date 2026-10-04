using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import;

namespace GodotAls.Animation.Lyra;

public partial class LyraWeaponResourcesSmoke:Node3D
{
    private readonly List<(LyraWeaponModelBinding Binding,LyraWeaponSequenceSampler Sampler)> _models=[];
    private int _frame,_samples,_bones,_published,_rejects,_retries;
    private double _positionError,_rotationError,_scaleError;
    private bool _render;
    private int _captures;
    public override void _Ready()
    {
        try
        {
            _render=OS.GetCmdlineUserArgs().Contains("--weapon-resource-render");
            var resources=new LyraWeaponResources();
            foreach(var entry in resources.Catalog.GetProperty("sequences").EnumerateObject())
            {
                var sampler=resources.CreateSampler(entry.Name);var output=new AlsPrecisePose[sampler.Names.Length];
                foreach(var row in sampler.Resource.GetProperty("native").EnumerateArray())
                {
                    sampler.Sample(row.GetProperty("time").GetDouble(),output);
                    var expected=row.GetProperty("pose").GetProperty("pose").EnumerateArray().Select(LyraLogicalSourceBank.ParsePose).ToArray();
                    Require(expected.Length==output.Length,"Native weapon bone count differs.");
                    for(int b=0;b<output.Length;b++)
                    {
                        var a=expected[b];var v=output[b];_positionError=Math.Max(_positionError,(a.Position-v.Position).LengthSquared);
                        _scaleError=Math.Max(_scaleError,(a.Scale-v.Scale).LengthSquared);
                        double e=0,n=0;double[] aq=[a.Rotation.X,a.Rotation.Y,a.Rotation.Z,a.Rotation.W],vq=[v.Rotation.X,v.Rotation.Y,v.Rotation.Z,v.Rotation.W];
                        for(int c=0;c<4;c++){e=Math.Max(e,Math.Abs(aq[c]-vq[c]));n=Math.Max(n,Math.Abs(aq[c]+vq[c]));}
                        _rotationError=Math.Max(_rotationError,Math.Min(e,n));_bones++;
                    }
                    _samples++;
                }
            }
            Require(Math.Sqrt(_positionError)<=2e-5&&_rotationError<=2e-6&&Math.Sqrt(_scaleError)<=2e-6,"Native weapon source sampling differs.");
            int index=0;
            foreach(var kind in new[]{"pistol","rifle","shotgun"})
            {
                var actor=new Node3D{Position=new((index++-1)*.9f,.5f,0)};AddChild(actor);
                var binding=new LyraWeaponModelBinding(actor,resources,kind);
                PreviewMaterial(binding.Model,kind);
                var path=resources.Catalog.GetProperty("sequences").EnumerateObject().Single(p=>p.Name.Contains("/"+kind[..1].ToUpperInvariant()+kind[1..]+"/")&&p.Name.Contains("_Reload.")).Name;
                var sampler=resources.CreateSampler(path);_models.Add((binding,sampler));
            }
            var camera=new Camera3D{Position=new(0,1.1f,2.2f),Current=true};AddChild(camera);camera.LookAt(new(0,.5f,0));
            AddChild(new DirectionalLight3D{RotationDegrees=new(-50,-25,0),LightEnergy=1.5f});
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            foreach(var (binding,sampler) in _models)
            {
                var output=new AlsPrecisePose[sampler.Names.Length];sampler.Sample(Math.Min(sampler.Length,_frame/60.0),output);
                var cancelled=binding.Stage(_frame,output);binding.Cancel();Reject(()=>binding.Publish(cancelled));
                var candidate=binding.Stage(_frame,output);Require(cancelled.Pose.SequenceEqual(candidate.Pose),"Weapon skin retry changed output.");
                Reject(()=>_models.First(m=>m.Binding!=binding).Binding.Publish(candidate));
                binding.Publish(candidate);Reject(()=>binding.Publish(candidate));_published++;_retries++;
            }
            _frame++;
            if(_render&&_frame is 1 or 60 or 120)_=Capture(_frame);
            if(_frame<180)return;
            Require(!_render||_captures==3,"Weapon render captures are incomplete.");
            GD.Print($"LYRA_WEAPON_RESOURCES_GODOT_OK clips=6 meshes=3 samples={_samples} bones={_bones} frames={_frame} publications={_published} retries={_retries} rejected={_rejects} positionCm={Math.Sqrt(_positionError):G17} rotation={_rotationError:G17} scale={Math.Sqrt(_scaleError):G17} native=True production=False");
            GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    // UE vertex colors are material masks. The resource preview uses the
    // exported diffuse/normal textures; original UE material parity is open.
    internal static void PreviewMaterial(Node model,string kind)
    {
        using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraWeaponResources.Root+"textures.json"));
        var rows=doc.RootElement.GetProperty("entries").EnumerateArray().Where(r=>r.GetProperty("kind").GetString()==kind).ToArray();
        string File(string channel)=>LyraWeaponResources.Root+rows.Single(r=>r.GetProperty("channel").GetString()==channel).GetProperty("file").GetString();
        var material=new StandardMaterial3D{AlbedoTexture=ResourceLoader.Load<Texture2D>(File("diffuse")),
            NormalEnabled=true,NormalTexture=ResourceLoader.Load<Texture2D>(File("normal")),VertexColorUseAsAlbedo=false};
        void Apply(Node n){if(n is MeshInstance3D mesh)mesh.MaterialOverride=material;foreach(var child in n.GetChildren())Apply(child);}Apply(model);
    }
    private async Task Capture(int frame)
    {
        try
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var path=ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/weapon-resource-preview-{frame}.png");
            Require(!System.IO.File.Exists(path),"Preserve weapon render evidence.");
            Require(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Weapon render save failed.");_captures++;
        }
        catch(Exception e){Fail(e);}
    }
    private void Reject(Action action){try{action();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException("Invalid weapon publication accepted.");}
    public override void _ExitTree(){foreach(var model in _models)model.Binding.Dispose();}
    private void Fail(Exception e){GD.PushError("Weapon resources failed: "+e);SetPhysicsProcess(false);GetTree().Quit(1);}
}

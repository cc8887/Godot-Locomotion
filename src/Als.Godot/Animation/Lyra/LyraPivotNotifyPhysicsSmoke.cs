using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraPivotNotifyPhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _roles=[];
    private readonly List<object> _rows=[];
    private int _hz=60,_frame,_retry,_pivot,_notify,_exit,_switches,_captures;
    private bool _render;
    private string? _report;
    private StaticBody3D? _floorBody;
    private BoxShape3D? _floorShape;
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();var hz=args.SingleOrDefault(a=>a.StartsWith("--pivot-hz="));if(hz is not null)_hz=int.Parse(hz.Split('=')[1]);
            Require(_hz is 30 or 60 or 120,"Invalid Pivot rate.");Engine.PhysicsTicksPerSecond=_hz;
            _report=args.SingleOrDefault(a=>a.StartsWith("--pivot-report="))?.Split('=',2)[1];_render=args.Contains("--pivot-render");
            _resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=2};_floorBody=floor;_floorShape=new(){Size=new(80,.2f,80)};
            floor.AddChild(new CollisionShape3D{Shape=_floorShape,Position=new(0,-.1f,0)});AddChild(floor);
            for(int i=0;i<6;i++)_roles.Add(new(this,_resources,catalog,"PivotRole"+i,new((i%3-1)*4,.92f,i/3*-4),new[]{"unarmed","pistol","rifle"}[i%3]));
            if(_render)
            {
                floor.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(80,.2f,80)},Position=new(0,-.1f,0)});
                var camera=new Camera3D{Position=new(0,7,12),Current=true};AddChild(camera);camera.LookAt(new(0,.8f,-4));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightEnergy=1.5f});RenderingServer.FramePostDraw+=Capture;
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Actual Pivot delta differs.");float delta=(float)dt;
            for(int i=0;i<6;i++)
            {
                var role=_roles[i];var animation=role.Animation;
                if(_frame==_hz*3||_frame==_hz*6)
                {
                    var history=animation.SourceNotifies.Queued;bool previous=animation.SourceNotifies.WasTransitionActiveInMainSourceState(7,4);
                    Require(animation.Rebind(animation.Profile=="rifle"?"pistol":"rifle"),"Pivot rebind failed.");
                    Require(history==animation.SourceNotifies.Queued&&previous==animation.SourceNotifies.WasTransitionActiveInMainSourceState(7,4),"Linked replacement erased Main notify history.");_switches++;
                }
                float direction=_frame<_hz/5?0:(_frame/(_hz*2)%2==0?1:-1);
                var observation=role.Move(new(new(0,direction),0,0,i>=3,false,false,false),delta);
                var oldQueue=animation.SourceNotifies.Queued;
                bool pivot=animation.SourceNotifies.WasTransitionActiveInMainSourceState(7,4);
                var first=observation.Prepare(animation,delta);animation.Cancel();
                Require(animation.SourceNotifies.Queued==oldQueue&&animation.SourceNotifies.WasTransitionActiveInMainSourceState(7,4)==pivot,"Cancelled role published notify history.");
                var candidate=observation.Prepare(animation,delta);
                Require(first.Output.Pose.SequenceEqual(candidate.Output.Pose)&&first.SourceNotifies.Queued.SequenceEqual(candidate.SourceNotifies.Queued)&&
                    first.Main.Main.Rules==candidate.Main.Main.Rules,"Pivot retry changed pose/queue/rules.");_retry++;
                Require(candidate.Main.Main.Rules.PivotNotify==pivot,"Production Main missed committed source-state query.");
                if(candidate.Main.Main.Machine.BeforeState==4)_pivot++;
                if(pivot)_notify++;
                if(candidate.Main.Main.Machine.Selected is {Edge:19}){Require(pivot,"Pivot notify edge triggered without history.");_exit++;}
                animation.Commit(candidate);
                _rows.Add(new{frame=_frame,role=i,state=animation.Host.Main.Machine.State,pivot,edge=candidate.Main.Main.Machine.Selected?.Edge,
                    queued=candidate.SourceNotifies.Queued.Select(q=>new{q.Core.PolicyIndex,q.Core.ActiveContext,q.Core.ReachedEnd,q.Playback.MainState,q.Playback.Epoch}),
                    profile=animation.Profile,position=new[]{role.Body.Position.X,role.Body.Position.Y,role.Body.Position.Z}});
            }
            _frame++;if(_frame>=_hz*8)Finish();
        }
        catch(Exception e){Fail(e);}
    }
    private void Capture()
    {if(_frame is not(31 or 151 or 271))return;var path=ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/pivot-notify-render-{_frame}.png");if(File.Exists(path))return;GetViewport().GetTexture().GetImage().SavePng(path);_captures++;}
    private void Finish()
    {
        Require(_pivot>0&&_notify>0&&_exit>0,"Actual physical Pivot notify path was not exercised.");
        var result=new{hz=_hz,roles=6,frames=_frame,moves=_roles.Sum(r=>r.Animation.CapsuleMoves),retries=_retry,pivotFrames=_pivot,notifyFrames=_notify,notifyExits=_exit,
            switches=_switches,actualAlsModel=true,actualJolt=true,wholeMainNative=false,historySha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_rows))))};
        if(_report is not null){Require(!File.Exists(_report),"Preserve Pivot report.");File.WriteAllText(_report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true})+"\n");}
        GD.Print($"LYRA_PIVOT_NOTIFY_PHYSICS_GODOT_OK hz={_hz} roles=6 frames={_frame} moves={result.moves} retries={_retry} pivotFrames={_pivot} notifyFrames={_notify} notifyExits={_exit} switches={_switches} captures={_captures}");GetTree().Quit();
    }
    private void Fail(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    public override void _ExitTree(){if(_render)RenderingServer.FramePostDraw-=Capture;foreach(var role in _roles)role.Dispose();_resources?.Dispose();
        if(_floorBody is not null&&GodotObject.IsInstanceValid(_floorBody))_floorBody.Free();_floorShape?.Dispose();}
}

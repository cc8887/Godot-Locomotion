using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

public partial class LyraCharacterGroundQuerySmoke:Node3D
{
    private CharacterBody3D? _body;
    private LyraCharacterSweep? _sweep;
    private LyraCharacterFloorProbe? _floor;
    private LyraCharacterGroundMovement? _ground;
    private CapsuleShape3D? _shape;
    private JsonDocument? _reference;
    private string _report=null!;
    private bool _done;
    private static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static double[] Values(AlsDoubleVector v)=>[v.X,v.Y,v.Z];
    private static void Require(bool v,string message){if(!v)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            _report=OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--ground-query-report="))["--ground-query-report=".Length..];
            Require(Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Preserve ground query evidence.");
            _reference=JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/character-ground-v1-reference.json")));
            var root=_reference.RootElement;Require(root.GetProperty("actualOriginalCMC").GetBoolean(),"Native movement provenance missing.");
            void Box(JsonElement b)
            {var e=V(b.GetProperty("extent"));var body=new StaticBody3D{Position=LyraGodotRigCollision.Position(V(b.GetProperty("center"))),CollisionLayer=1,CollisionMask=2};
             body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=new((float)(e.Y*.02),(float)(e.Z*.02),(float)(e.X*.02))}});AddChild(body);}
            Box(root.GetProperty("floor"));foreach(var box in root.GetProperty("obstacles").EnumerateArray())Box(box);
            _body=new(){CollisionLayer=2,CollisionMask=1,Position=new(0,.92f,0)};
            _shape=new(){Radius=.35f,Height=1.8f};_body.AddChild(new CollisionShape3D{Shape=_shape});AddChild(_body);
            _floor=new(_body,.35f);_sweep=new(_body,.35f);_ground=new(_body,_floor,_sweep);
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        if(_done||_body is null||_reference is null||_ground is null||_sweep is null||_floor is null||_shape is null)return;
        try
        {
            int mismatches=0;double maxPosition=0,maxVelocity=0,maxTime=0,maxNormal=0,maxFloor=0;var rows=new List<object>();
            foreach(var row in _reference.RootElement.GetProperty("rows").EnumerateArray())
            {
                var start=LyraGodotRigCollision.Position(V(row.GetProperty("start")));var motion=LyraGodotRigCollision.Position(V(row.GetProperty("motion")));
                float half=row.GetProperty("halfHeight").GetSingle()*.01f,delta=row.GetProperty("delta").GetSingle();
                _shape.Height=half*2;_body.GlobalPosition=start;_body.Velocity=motion/delta;
                bool grounded=row.GetProperty("kind").GetString()=="ground";
                var before=_floor.Find(start,half,true);LyraCharacterSweepHit hit=default;LyraCharacterGroundMove ground=default;
                if(grounded){ground=_ground.Walk(_body.Velocity,_body.Velocity,delta,half,false);_body.Velocity=ground.Velocity;}
                else
                {
                    var transform=_body.GlobalTransform;var query=_sweep.Query(motion,half);
                    Require(transform==_body.GlobalTransform,"Sweep query moved its owner.");
                    hit=_sweep.Move(motion,half);Require(hit==query,"Same actual sweep changed without geometry mutation.");
                }
                // Recorded physical results are read only after the actual move.
                var after=LyraGodotRigCollision.NativePosition(_body.GlobalPosition);var velocity=LyraGodotRigCollision.NativePosition(_body.Velocity);
                double p=Math.Sqrt((after-V(row.GetProperty("after"))).LengthSquared),v=Math.Sqrt((velocity-V(row.GetProperty("velocity"))).LengthSquared);
                double time=grounded?0:Math.Abs(hit.Time-row.GetProperty("time").GetDouble());
                double normal=grounded||!hit.Blocking?0:Math.Sqrt((LyraGodotRigCollision.NativeVector(hit.Normal)-V(row.GetProperty("normal"))).LengthSquared);
                double gap=grounded?Math.Abs((ground.Floor.Floor.FloorDistance+ground.Floor.Movement.Y)*100d-row.GetProperty("floorDistance").GetDouble()):0;
                bool flags=grounded?ground.Floor.Grounded==row.GetProperty("walkable").GetBoolean():
                    hit.Blocking==row.GetProperty("blocking").GetBoolean()&&hit.Penetrating==row.GetProperty("penetrating").GetBoolean();
                bool match=p<=.01&&v<=.001&&time<=.0001&&normal<=.0001&&gap<=.01&&flags;
                mismatches+=match?0:1;maxPosition=Math.Max(maxPosition,p);maxVelocity=Math.Max(maxVelocity,v);maxTime=Math.Max(maxTime,time);maxNormal=Math.Max(maxNormal,normal);maxFloor=Math.Max(maxFloor,gap);
                rows.Add(new{native=row,actual=new{after=Values(after),velocity=Values(velocity),hit.Time,hit.RawTime,hit.Blocking,hit.Penetrating,
                    normal=Values(LyraGodotRigCollision.NativeVector(hit.Normal)),impactNormal=Values(LyraGodotRigCollision.NativeVector(hit.ImpactNormal)),
                    stepped=ground.Stepped,reverted=ground.StepReverted,grounded=ground.Floor.Grounded,contacts=ground.Contacts?.Length??0},p,v,time,normal,gap,flags,match});
            }
            using var stream=new FileStream(_report,FileMode.CreateNew);
            JsonSerializer.Serialize(stream,new{queries=rows.Count,mismatches,maxPositionCm=maxPosition,maxVelocityCmps=maxVelocity,maxTime,maxNormal,maxFloorCm=maxFloor,
                actualJolt=true,replayedPhysicalObservations=false,comparisonPassed=mismatches==0,nativeWorldTrajectoryParity=false,completeAcceptance=false,rows});
            GD.Print($"LYRA_CHARACTER_GROUND_QUERY_DIAGNOSTIC queries={rows.Count} mismatches={mismatches} maxP={maxPosition:R} maxV={maxVelocity:R} maxTime={maxTime:R} maxNormal={maxNormal:R} maxFloor={maxFloor:R}");
            _done=true;GetTree().Quit(mismatches==0?0:1);
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Ground query failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){_sweep?.Dispose();_floor?.Dispose();_shape?.Dispose();_reference?.Dispose();}
}

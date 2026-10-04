using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

public partial class LyraCharacterFloorQuerySmoke:Node3D
{
    private CharacterBody3D? _body;
    private CapsuleShape3D? _capsule;
    private LyraCharacterFloorProbe? _probe;
    private JsonDocument? _reference;
    private string _report=null!,_tag=null!;
    private bool _done;
    private static AlsDoubleVector V(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    private static double[] Values(AlsDoubleVector v)=>[v.X,v.Y,v.Z];
    private static void Require(bool v,string message){if(!v)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            _report=args.Single(a=>a.StartsWith("--floor-query-report="))["--floor-query-report=".Length..];
            _tag=args.Single(a=>a.StartsWith("--floor-query-tag="))["--floor-query-tag=".Length..];
            Require(Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Preserve floor-query evidence.");
            _reference=JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/character-floor-v1-reference.json")));
            var root=_reference.RootElement;Require(root.GetProperty("actualOriginalCMC").GetBoolean(),"Original floor reference missing.");
            void Box(JsonElement b)
            {
                var e=V(b.GetProperty("extent"));var body=new StaticBody3D{Position=LyraGodotRigCollision.Position(V(b.GetProperty("center"))),CollisionLayer=1,CollisionMask=2};
                body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=new((float)(e.Y*.02),(float)(e.Z*.02),(float)(e.X*.02))}});AddChild(body);
            }
            Box(root.GetProperty("floor"));foreach(var b in root.GetProperty("obstacles").EnumerateArray())Box(b);
            var settings=LyraCharacterMovementSettings.Default;
            _body=new(){Position=new(0,.92f,0),CollisionLayer=2,CollisionMask=1};
            _capsule=new(){Radius=settings.Radius,Height=settings.StandingHalfHeight*2};
            _body.AddChild(new CollisionShape3D{Shape=_capsule});AddChild(_body);_probe=new(_body,settings.Radius);
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_done||_probe is null||_body is null||_reference is null)return;
        try
        {
            var rows=new List<object>();int mismatches=0;double maxGap=0,maxLine=0,maxPoint=0,maxNormal=0,maxTime=0;
            foreach(var row in _reference.RootElement.GetProperty("rows").EnumerateArray())
            {
                var before=_body.GlobalTransform;var velocity=_body.Velocity;
                var p=LyraGodotRigCollision.Position(V(row.GetProperty("location")));float half=row.GetProperty("halfHeight").GetSingle()*.01f;
                bool find=row.GetProperty("query").GetString()=="find";
                var actual=find?_probe.Find(p,half,row.GetProperty("walking").GetBoolean()):
                    _probe.Compute(p,half,row.GetProperty("lineDistance").GetSingle()*.01f,row.GetProperty("sweepDistance").GetSingle()*.01f);
                Require(before==_body.GlobalTransform&&velocity==_body.Velocity,"Floor query mutated its actor.");
                double gap=Math.Abs(actual.FloorDistance*100d-row.GetProperty("floorDistance").GetDouble());
                double line=Math.Abs(actual.LineDistance*100d-row.GetProperty("lineDistanceResult").GetDouble());
                double point=Math.Sqrt((LyraGodotRigCollision.NativePosition(actual.Hit.Point)-V(row.GetProperty("point"))).LengthSquared);
                double normal=Math.Sqrt((LyraGodotRigCollision.NativeVector(actual.Hit.Normal)-V(row.GetProperty("normal"))).LengthSquared);
                double time=Math.Abs(actual.Hit.Time-row.GetProperty("time").GetDouble());
                bool flags=actual.Blocking==row.GetProperty("blocking").GetBoolean()&&actual.Walkable==row.GetProperty("walkable").GetBoolean()&&
                    actual.LineTrace==row.GetProperty("lineTrace").GetBoolean()&&actual.Hit.Penetrating==row.GetProperty("penetrating").GetBoolean();
                bool valid=row.GetProperty("blocking").GetBoolean();
                // Invalid overlap contacts have no usable floor. Compare their
                // classification/distances; retain their raw point diagnostics.
                bool match=flags&&gap<=.01&&line<=.01&&(!valid||point<=.01&&normal<=.0001&&time<=.0001);
                mismatches+=match?0:1;maxGap=Math.Max(maxGap,gap);maxLine=Math.Max(maxLine,line);
                if(valid){maxPoint=Math.Max(maxPoint,point);maxNormal=Math.Max(maxNormal,normal);maxTime=Math.Max(maxTime,time);}
                rows.Add(new{native=row,actual=new{actual.Blocking,actual.Walkable,actual.LineTrace,actual.FloorDistance,actual.LineDistance,
                    actual.Hit.Penetrating,actual.Hit.Time,point=Values(LyraGodotRigCollision.NativePosition(actual.Hit.Point)),normal=Values(LyraGodotRigCollision.NativeVector(actual.Hit.Normal))},
                    gap,line,point,normal,time,flags,valid,match});
            }
            var result=new{tag=_tag,queries=rows.Count,mismatches,physicsQueries=_probe.Queries,perchQueries=_probe.PerchQueries,
                maxDistanceCm=maxGap,maxLineDistanceCm=maxLine,maxValidPointCm=maxPoint,maxValidNormal=maxNormal,maxValidTime=maxTime,
                actualJolt=true,queryMutatesActor=false,floorSettingsSha256=_probe.Settings.Sha256,comparisonPassed=mismatches==0,
                nativeWorldTrajectoryParity=false,completeAcceptance=false,rows};
            using var stream=new FileStream(_report,FileMode.CreateNew);JsonSerializer.Serialize(stream,result);
            GD.Print($"LYRA_CHARACTER_FLOOR_QUERY_DIAGNOSTIC queries={rows.Count} mismatches={mismatches} maxD={maxGap:R} maxP={maxPoint:R} maxN={maxNormal:R} maxT={maxTime:R} actualJolt=true queryMutatesActor=false");
            _done=true;GetTree().Quit(mismatches==0?0:1);
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Actual floor query failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){_probe?.Dispose();_reference?.Dispose();_capsule?.Dispose();}
}

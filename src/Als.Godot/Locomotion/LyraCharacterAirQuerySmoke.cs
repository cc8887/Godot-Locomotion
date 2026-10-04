using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

public partial class LyraCharacterAirQuerySmoke:Node3D
{
    private CharacterBody3D? _body;
    private CapsuleShape3D? _shape;
    private LyraCharacterSweep? _sweep;
    private LyraCharacterFloorProbe? _floor;
    private LyraCharacterGroundMovement? _ground;
    private JsonDocument? _reference;
    private string _report=null!;
    private bool _done;
    private static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static double[] Values(AlsDoubleVector v)=>[v.X,v.Y,v.Z];
    public override void _Ready()
    {
        try
        {
            _report=OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--air-query-report="))["--air-query-report=".Length..];
            if(!Path.IsPathFullyQualified(_report)||File.Exists(_report))throw new InvalidOperationException("Preserve air evidence.");
            _reference=JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/character-air-v3-reference.json")));
            var root=_reference.RootElement;
            if(!root.GetProperty("actualOriginalPhysFalling").GetBoolean())throw new InvalidDataException("Missing original PhysFalling provenance.");
            void Box(JsonElement box)
            {
                var e=V(box.GetProperty("extent"));var rotation=V(box.GetProperty("rotation"));
                // These controlled fixtures use only UE Pitch; UE -Pitch maps to Godot -X.
                if(rotation.Y!=0||rotation.Z!=0)throw new NotSupportedException("Only authored pitch fixtures supported.");
                var body=new StaticBody3D{Position=LyraGodotRigCollision.Position(V(box.GetProperty("center"))),
                    Rotation=new(Mathf.DegToRad((float)rotation.X),0,0),CollisionLayer=1,CollisionMask=2};
                body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=new((float)e.Y*.02f,(float)e.Z*.02f,(float)e.X*.02f)}});
                AddChild(body);
            }
            Box(root.GetProperty("floor"));foreach(var box in root.GetProperty("obstacles").EnumerateArray())Box(box);
            _body=new(){CollisionLayer=2,CollisionMask=1,Position=new(0,.92f,0)};
            _shape=new(){Radius=.35f,Height=1.8f};_body.AddChild(new CollisionShape3D{Shape=_shape});AddChild(_body);
            _floor=new(_body,.35f);_sweep=new(_body,.35f);_ground=new(_body,_floor,_sweep);
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        if(_done||_body is null||_shape is null||_floor is null||_sweep is null||_ground is null||_reference is null)return;
        try
        {
            int mismatches=0,apexSplits=0,landings=0,multipleContacts=0;double maxP=0,maxV=0;var rows=new List<object>();
            foreach(var row in _reference.RootElement.GetProperty("rows").EnumerateArray())
            {
                int teleports=_sweep.TeleportRecoveries,swept=_sweep.SweptRecoveries,combined=_sweep.CombinedRecoveries,
                    adjusted=_sweep.AdjustedRecoveries,original=_sweep.OriginalRecoveries;
                float half=row.GetProperty("halfHeight").GetSingle()*.01f;
                _shape.Height=half*2;_body.GlobalPosition=LyraGodotRigCollision.Position(V(row.GetProperty("start")));
                var input=new LyraCharacterFallingInput(V(row.GetProperty("velocity")),V(row.GetProperty("acceleration")),
                    row.GetProperty("analog").GetSingle(),half<.9f);
                var air=new LyraCharacterAirMovement(_body,_floor,_sweep,_ground,LyraCharacterMovementSettings.Default,12345);
                var actual=air.Fall(input,row.GetProperty("delta").GetSingle(),half);
                _body.Velocity=LyraGodotRigCollision.Position(actual.Velocity);
                // Native outputs are consulted only after real scene movement.
                var position=LyraGodotRigCollision.NativePosition(_body.GlobalPosition);
                double p=Math.Sqrt((position-V(row.GetProperty("after"))).LengthSquared),
                    v=Math.Sqrt((actual.Velocity-V(row.GetProperty("output"))).LengthSquared);
                bool flags=actual.Floor.Grounded==row.GetProperty("grounded").GetBoolean()&&actual.ApexSplits==row.GetProperty("apexAttempts").GetInt32()
                    &&air.RandomSeed==row.GetProperty("randomSeedAfter").GetUInt32();
                bool match=p<=.01&&v<=.001&&flags;
                mismatches+=match?0:1;maxP=Math.Max(maxP,p);maxV=Math.Max(maxV,v);apexSplits+=actual.ApexSplits;
                landings+=actual.Floor.Grounded?1:0;multipleContacts+=actual.Contacts.Length>1?1:0;
                rows.Add(new{native=row,actual=new{position=Values(position),velocity=Values(actual.Velocity),
                    bodyVelocity=Values(LyraGodotRigCollision.NativePosition(_body.Velocity)),grounded=actual.Floor.Grounded,
                    actual.Substeps,actual.ApexSplits,actual.LandingRemaining,actual.RandomEscape,randomSeedAfter=air.RandomSeed,contacts=actual.Contacts.Length,
                    recovery=new{teleport=_sweep.TeleportRecoveries-teleports,swept=_sweep.SweptRecoveries-swept,combined=_sweep.CombinedRecoveries-combined,
                        adjusted=_sweep.AdjustedRecoveries-adjusted,original=_sweep.OriginalRecoveries-original}},p,v,flags,match});
            }
            using var stream=new FileStream(_report,FileMode.CreateNew);
            JsonSerializer.Serialize(stream,new{queries=rows.Count,mismatches,maxPositionCm=maxP,maxVelocityCmps=maxV,apexSplits,landings,multipleContacts,
                actualJolt=true,replayedPhysicalObservations=false,comparisonPassed=mismatches==0,rootMotionCases=false,completeAcceptance=false,rows});
            GD.Print($"LYRA_CHARACTER_AIR_QUERY_DIAGNOSTIC queries={rows.Count} mismatches={mismatches} maxP={maxP:R} maxV={maxV:R} apex={apexSplits} landings={landings} multiContact={multipleContacts}");
            _done=true;GetTree().Quit(mismatches==0?0:1);
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Air query failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){_sweep?.Dispose();_floor?.Dispose();_shape?.Dispose();_reference?.Dispose();}
}

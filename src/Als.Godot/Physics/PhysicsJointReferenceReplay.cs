using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Diagnostic comparison, not a claim of backend equivalence. Starts both
// backends from the native recorded body transforms and zero velocities.
public partial class PhysicsJointReferenceReplay : Node3D
{
    private sealed record Rig(AlsRagdollPhysicsDefinition Definition,AlsPhysicsJointSettings[] Settings,
        string[] Names,int[] Parents,AlsLocalPose[] Rest);
    private sealed record Result(int Case,string Mesh,string Child,int Hz,bool FastDrive,bool BodyConditioning,
        double MaxRotationErrorRad,double FinalRotationErrorRad,double MaxPositionErrorM,
        double MaxLinearVelocityErrorMps,double MaxAngularVelocityErrorRadps);
    private readonly Dictionary<string,Rig> _rigs=[];
    private readonly List<Result> _results=[];
    private JsonDocument? _reference;
    private AlsPhysicsBodySet? _bodies;private AlsPhysicsJointSet? _joints;
    private int _case,_frame;private bool _done,_conditionBodies,_computedConditioning,_singleCase,_assertRelease;private string _output="";
    private double _maxRotation,_maxPosition,_maxLinear,_maxAngular;

    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_conditionBodies=args.Contains("--body-conditioning");
            _computedConditioning=args.Contains("--computed-body-conditioning");
            if(_computedConditioning&&_conditionBodies)throw new ArgumentException("Select computed or recorded body conditioning, not both.");
            _output=args.FirstOrDefault(a=>a.StartsWith("--report="))?[9..]??"";
            if(!System.IO.Path.IsPathFullyQualified(_output)||System.IO.File.Exists(_output))throw new ArgumentException("Replay requires a new absolute --report path.");
            _reference=JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_joint_solver_reference.json"));
            var trace=args.FirstOrDefault(a=>a.StartsWith("--trace-case="));
            if(trace is not null){_case=int.Parse(trace[13..]);_singleCase=true;}
            _assertRelease=args.Contains("--assert-limit-release");
            if(_assertRelease)
            {
                _case=Array.FindIndex(_reference.RootElement.GetProperty("cases").EnumerateArray().ToArray(),r=>
                    r.GetProperty("mesh").GetString()==AlsPhysicsAssetCompiler.MeshRoot+"Mannequin.Mannequin"&&
                    r.GetProperty("child").GetString()=="spine_02"&&r.GetProperty("hz").GetInt32()==120&&
                    r.GetProperty("axis").GetInt32()==2&&r.GetProperty("perturbation").GetDouble()<0&&!r.GetProperty("fullSpeedDrive").GetBoolean());
                if(_case<0)throw new InvalidDataException("Missing native limit-release case.");
                _singleCase=true;
            }
            var set=ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            foreach(var name in new[]{"Mannequin","AnimMan"})
            {
                var definition=AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"),AlsPhysicsAssetCompiler.MeshRoot+name+"."+name);
                var settings=AlsPhysicsJointCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_joint_reference.json"),definition);
                var asset=set.SkeletalMeshes.Single(m=>m.ObjectPath==definition.Mesh);
                var model=ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot+"/"+asset.ResourcePath).Instantiate<Node3D>();AddChild(model);
                var skeleton=AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                var names=Enumerable.Range(0,skeleton.GetBoneCount()).Select(i=>skeleton.GetBoneName(i).ToString()).ToArray();
                _rigs.Add(definition.Mesh,new(definition,settings,names,Enumerable.Range(0,names.Length).Select(skeleton.GetBoneParent).ToArray(),
                    Enumerable.Range(0,names.Length).Select(i=>AlsPhysicsBodySet.Pose(skeleton.GetBoneRest(i))).ToArray()));
                model.Free();
            }
            Engine.PhysicsTicksPerSecond=Current.GetProperty("hz").GetInt32();
        }
        catch(Exception e){Fail(e);}
    }
    private JsonElement Current=>_reference!.RootElement.GetProperty("cases")[_case];
    private void StartCase()
    {
        var row=Current;var rig=_rigs[row.GetProperty("mesh").GetString()!];var child=row.GetProperty("child").GetString();
        var joint=rig.Definition.Joints.Single(j=>rig.Definition.Bodies[j.ChildBody].Bone==child);
        var definition=rig.Definition with {Bodies=[rig.Definition.Bodies[joint.ParentBody] with {Index=0,PhysicsType=1},rig.Definition.Bodies[joint.ChildBody] with {Index=1}],
            Joints=[joint with {Index=0,ParentBody=0,ChildBody=1}],DisabledCollisions=[(0,1)]};
        Engine.PhysicsTicksPerSecond=row.GetProperty("hz").GetInt32();
        _bodies=new(this,definition,rig.Names,rig.Parents,1,1,collisionLayer:0,collisionMask:0);
        _bodies.Seed(new(1,1,1),Transform3D.Identity,rig.Rest,Vector3.Zero,Vector3.Zero);_bodies.Start();
        for(var i=0;i<2;i++)
        {
            var b=_bodies.BodyAt(i);b.FreezeMode=RigidBody3D.FreezeModeEnum.Static;b.GravityScale=0;
            var recorded=row.GetProperty("samples")[0].GetProperty(i==0?"parent":"child");
            b.GlobalTransform=ReadTransform(recorded.GetProperty("world"))*_bodies.BoneToMass(i);
            b.LinearVelocity=Vector3.Zero;b.AngularVelocity=Vector3.Zero;
            if(i==1&&_conditionBodies)
            {
                var inv=Vector(row.GetProperty("bodies")[i].GetProperty("bodyConditionedInverseInertia"));
                b.Inertia=new(.0001f/inv.X,.0001f/inv.Y,.0001f/inv.Z);
            }
        }
        _joints=new(_bodies,definition,[rig.Settings[joint.Index] with {Index=0}],conditionBodyInertia:_computedConditioning);
        if(_computedConditioning)
        {
            var inverse=Vector(row.GetProperty("bodies")[1].GetProperty("bodyConditionedInverseInertia"));
            var expected=new Vector3(.0001f/inverse.X,.0001f/inverse.Y,.0001f/inverse.Z);
            if((_bodies.BodyAt(1).Inertia-expected).Length()>expected.Length()*1e-5f)
                throw new InvalidOperationException("Computed body inertia differs from native pair.");
        }
        if(row.GetProperty("fullSpeedDrive").GetBoolean())_joints.SetEffectiveAngularDrive(37500,0);
        _frame=0;_maxRotation=_maxPosition=_maxLinear=_maxAngular=0;
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_done)return;
        try
        {
            // Create the next pair in this callback, immediately before recording
            // frame zero. Creating it at the end of the previous case lets Jolt
            // apply a hard-constraint step that the trace has not counted.
            if(_bodies is null)StartCase();
            var row=Current;var sample=row.GetProperty("samples")[_frame].GetProperty("child");
            if(Math.Abs(delta-row.GetProperty("dt").GetDouble())>1e-7)throw new InvalidOperationException("Replay step differs from recorded step.");
            var actual=_bodies!.BodyAt(1);var pose=actual.GlobalTransform*_bodies.BoneToMass(1).AffineInverse();var expected=ReadTransform(sample.GetProperty("world"));
            var dot=pose.Basis.Orthonormalized().GetRotationQuaternion().Dot(expected.Basis.Orthonormalized().GetRotationQuaternion());
            var rotation=2*Math.Acos(Math.Clamp(Math.Abs(dot),0,1));
            _maxRotation=Math.Max(_maxRotation,rotation);_maxPosition=Math.Max(_maxPosition,pose.Origin.DistanceTo(expected.Origin));
            var linear=Vector(sample.GetProperty("linearVelocity"))*new Vector3(.01f,-.01f,.01f);
            var angular=Vector(sample.GetProperty("angularVelocity"))*new Vector3(-1,1,-1);
            _maxLinear=Math.Max(_maxLinear,actual.LinearVelocity.DistanceTo(linear));_maxAngular=Math.Max(_maxAngular,actual.AngularVelocity.DistanceTo(angular));
            if(!pose.IsFinite()||!double.IsFinite(_maxRotation+_maxPosition+_maxLinear+_maxAngular))throw new InvalidOperationException("Nonfinite replay state.");
            if(_frame==1)_joints!.VerifyInertiaReadback();
            if(_frame==0&&(_maxRotation>.001||_maxPosition>.0001||_maxLinear>.0001||_maxAngular>.0001))
                throw new InvalidOperationException($"Initial body state differs at case {_case}: rotation={_maxRotation} position={_maxPosition} linear={_maxLinear} angular={_maxAngular}.");
            if(_singleCase)
            {
                var f=_joints!.Frames(0);var p=f.Parent.Basis.Orthonormalized().GetRotationQuaternion();var c=f.Child.Basis.Orthonormalized().GetRotationQuaternion();
                if(p.Dot(c)<0)c=-c;
                var angles=AlsJointAngularKinematics.Evaluate(new AlsQuaternion(-p.X,p.Y,-p.Z,p.W).Normalized(),new AlsQuaternion(-c.X,c.Y,-c.Z,c.W).Normalized()).Angles;
                if(_assertRelease&&_frame==3&&(angles.Z<=0||row.GetProperty("samples")[_frame].GetProperty("angles")[2].GetDouble()<=0))
                    throw new InvalidOperationException("Inward limit failed to release momentum across the native zero crossing.");
                GD.Print($"JOINT_REPLAY_TICK frame={_frame} actual_angles={angles} native_angles={row.GetProperty("samples")[_frame].GetProperty("angles")} rotation_error={rotation}");
            }
            if(_frame++<12){_joints!.Step(delta);return;}
            _results.Add(new(_case,row.GetProperty("mesh").GetString()!,row.GetProperty("child").GetString()!,Engine.PhysicsTicksPerSecond,
                row.GetProperty("fullSpeedDrive").GetBoolean(),_conditionBodies||_computedConditioning,_maxRotation,rotation,_maxPosition,_maxLinear,_maxAngular));
            _joints!.Dispose();_bodies.Dispose();_joints=null;_bodies=null;
            if(++_case<_reference!.RootElement.GetProperty("cases").GetArrayLength()&&!_singleCase)
            {Engine.PhysicsTicksPerSecond=Current.GetProperty("hz").GetInt32();return;}
            using(var file=Godot.FileAccess.Open(_output,Godot.FileAccess.ModeFlags.Write))file.StoreString(JsonSerializer.Serialize(_results,new JsonSerializerOptions{WriteIndented=true}));
            GD.Print($"JOINT_REFERENCE_REPLAY_RECORDED cases={_results.Count} body_conditioning={_conditionBodies} computed_conditioning={_computedConditioning} max_rotation_error_rad={_results.Max(r=>r.MaxRotationErrorRad)} max_position_error_m={_results.Max(r=>r.MaxPositionErrorM)} parity_asserted=false");
            if(_assertRelease)GD.Print("JOINT_LIMIT_RELEASE_OK native_zero_crossing_frame=3");
            _done=true;GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private static Vector3 Vector(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    private static Transform3D ReadTransform(JsonElement e)
    {
        var p=Vector(e.GetProperty("position"));var q=e.GetProperty("rotation");
        return new(new Basis(new Quaternion(-q[0].GetSingle(),q[1].GetSingle(),-q[2].GetSingle(),q[3].GetSingle()).Normalized()),p*new Vector3(.01f,-.01f,.01f));
    }
    private void Fail(Exception e){GD.PushError("JOINT_REFERENCE_REPLAY_FAILED "+e);_done=true;GetTree().Quit(1);}
    public override void _ExitTree(){_joints?.Dispose();_bodies?.Dispose();_reference?.Dispose();}
}

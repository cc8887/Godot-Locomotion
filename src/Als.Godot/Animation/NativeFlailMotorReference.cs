using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal static class NativeFlailMotorReference
{
    internal static void Run(AlsMovementGraphDefinition graph, AlsAnimationSetDefinition set)
    {
        static string Read(string file)=>Godot.FileAccess.GetFileAsString("res://assets/config/"+file);
        using var document=JsonDocument.Parse(Read("v4_physics_motor_targets.json"));
        var skeleton=graph.RagdollRawSources.GetSkeleton(graph.RagdollPose.SkeletonId);
        using var source=new AlsRagdollAnimationSource(graph,set,[]);
        var logical=new AlsLocalPose[skeleton.LogicalBoneCount];
        double maxPosition=0,maxRotation=0,maxTarget=0; var samples=0; var targetsChecked=0;
        foreach(var rig in document.RootElement.GetProperty("rigs").EnumerateArray())
        {
            if(rig.GetProperty("playLength").GetSingle()!=graph.RagdollFrame.Sequence.DurationSeconds)
                throw new InvalidDataException("Native Flail duration differs from source clock.");
            var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs.json"),rig.GetProperty("mesh").GetString()!);
            var settings=AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference.json"),definition);
            var converter=new AlsNativeMotorPose(definition,skeleton.LogicalBoneNames,skeleton.LogicalParents);
            var locals=new AlsPrecisePose[definition.Bones.Length];
            var motors=new AlsRagdollMotorInputs(definition,settings,1.5f,1.5f);
            var targets=settings.Select(s=>s.AngularDrive.Target).ToArray();
            var output=new AlsIslandAngularDrive[motors.OutputCount];
            foreach(var sample in rig.GetProperty("motorSamples").EnumerateArray())
            {
                var time=sample.GetProperty("time").GetSingle(); source.Sample(time,logical,[]); converter.Convert(logical,locals);
                double p=0,q=0,t=0; var worst="";
                var bones=sample.GetProperty("bones");
                for(var i=0;i<locals.Length;i++)
                {
                    if(bones[i].GetProperty("name").GetString()!=definition.Bones[i].Name ||
                        bones[i].GetProperty("parent").GetInt32()!=definition.Bones[i].Parent)
                        throw new InvalidDataException("Native sample bone binding differs.");
                    var expected=bones[i].GetProperty("local"); var v=expected.GetProperty("translation");
                    var delta=locals[i].Position-new AlsDoubleVector(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
                    p=Math.Max(p,Math.Sqrt(delta.LengthSquared));
                    var error=Error(locals[i].Rotation,Q(expected.GetProperty("rotation")));
                    if(error>q) { q=error; worst=definition.Bones[i].Name; }
                }
                motors.EvaluateParameters(locals,targets,sample.GetProperty("spring").GetSingle(),sample.GetProperty("damping").GetSingle(),output);
                foreach(var motor in output)
                {
                    var expected=sample.GetProperty("motors")[motor.Joint].GetProperty("target").GetProperty("rotation");
                    t=Math.Max(t,Error(motor.Target,Q(expected))); targets[motor.Joint]=motor.Target; targetsChecked++;
                }
                GD.Print($"FLAIL_NATIVE_SAMPLE mesh={definition.Mesh} time={time:R} position_cm={p:R} rotation_component={q:R} target_component={t:R} worst_bone={worst}");
                maxPosition=Math.Max(maxPosition,p); maxRotation=Math.Max(maxRotation,q); maxTarget=Math.Max(maxTarget,t); samples++;
            }
        }
        GD.Print($"FLAIL_NATIVE_SUMMARY samples={samples} targets={targetsChecked} position_cm={maxPosition:R} rotation_component={maxRotation:R} target_component={maxTarget:R}");
        if(samples!=10 || targetsChecked!=180 || !double.IsFinite(maxPosition) || !double.IsFinite(maxRotation) ||
            !double.IsFinite(maxTarget) || maxPosition>2e-5 || maxRotation>2e-7 || maxTarget>2e-7)
            throw new InvalidOperationException("Actual sampled Flail rotations/targets differ from native reference.");
    }
    private static AlsQuaternion Q(JsonElement q)=>new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble());
    private static double Error(AlsQuaternion actual,AlsQuaternion expected)
    {
        if(AlsQuaternion.Dot(actual,expected)<0) actual=-actual;
        var d=actual+-expected;
        return Math.Max(Math.Max(Math.Abs(d.X),Math.Abs(d.Y)),Math.Max(Math.Abs(d.Z),Math.Abs(d.W)));
    }
}

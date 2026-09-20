using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsConditionedBodyInertia(AlsDoubleVector ExtentsCm,Vector3 InverseInertiaScale);

public static class AlsBodyInertiaCompiler
{
    public const string Coordinates="UE actor-local bounds; COM-local extents; cm, kg, inverse kg*cm^2";
    // Native cooked actor bounds are geometry input, not a table of correction
    // factors. Recompute all connector arms from the actual bound topology.
    public static AlsConditionedBodyInertia[] Compile(string json,AlsRagdollPhysicsDefinition definition,AlsPhysicsJointSettings[] joints)
    {
        try{return CompileDocument(json,definition,joints);}
        catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {throw new InvalidDataException("Malformed native inertia input.",e);}
    }
    private static AlsConditionedBodyInertia[] CompileDocument(string json,AlsRagdollPhysicsDefinition definition,AlsPhysicsJointSettings[] joints)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32()==1&&root.GetProperty("coordinates").GetString()==Coordinates,"Inertia schema/units differ.");
        Require(joints.Length==definition.Joints.Length,"Inertia topology settings differ.");
        var rig=root.GetProperty("rigs").EnumerateArray().Single(r=>r.GetProperty("mesh").GetString()==definition.Mesh&&r.GetProperty("isolatedChild").GetString()=="");
        Require(rig.GetProperty("physicsAsset").GetString()==definition.PhysicsAsset,"Inertia physics asset differs.");
        var bodies=rig.GetProperty("bodies").EnumerateArray().ToDictionary(b=>b.GetProperty("bone").GetString()!);
        var settings=Settings(root.GetProperty("settings"));var result=new AlsConditionedBodyInertia[definition.Bodies.Length];
        foreach(var body in definition.Bodies)
        {
            var native=bodies[body.Bone];
            Require(native.GetProperty("nativeMassKg").GetDouble()==body.MassKg&&V(native.GetProperty("nativeInertiaKgCm2"))==body.InertiaKgCm2,
                "Inertia geometry reference mass differs from body definition.");
            var extents=AlsBodyInertiaConditioning.CollisionExtents(V(native.GetProperty("localBoundsMin")),V(native.GetProperty("localBoundsMax")),body.MassLocal.Rotation);
            foreach(var joint in definition.Joints)
            {
                Require(joints[joint.Index].Index==joint.Index,"Inertia joint identity differs.");
                var motion=joints[joint.Index].LinearMotion;
                if(motion==new AlsJointMotions(AlsJointMotion.Free,AlsJointMotion.Free,AlsJointMotion.Free))continue;
                if(joint.ParentBody==body.Index)extents=AlsBodyInertiaConditioning.IncludeConnector(extents,joint.ParentFrame.Position,body.MassLocal);
                if(joint.ChildBody==body.Index)extents=AlsBodyInertiaConditioning.IncludeConnector(extents,joint.ChildFrame.Position,body.MassLocal);
            }
            var inverse=new Vector3((float)(1/body.InertiaKgCm2.X),(float)(1/body.InertiaKgCm2.Y),(float)(1/body.InertiaKgCm2.Z));
            var enabled=settings.Enabled&&body.Defaults.GetProperty("bInertiaConditioning").GetBoolean();
            var scale=body.PhysicsType==1?Vector3.One:AlsBodyInertiaConditioning.Calculate((float)(1/body.MassKg),inverse,extents.ToSingle(),settings with{Enabled=enabled});
            result[body.Index]=new(extents,scale);
        }
        return result;
    }
    public static AlsBodyInertiaSettings Settings(JsonElement e)=>new(e.GetProperty("enabled").GetBoolean(),
        e.GetProperty("maxDistanceCm").GetSingle(),e.GetProperty("maxRotationRatio").GetSingle(),e.GetProperty("maxInvInertiaComponentRatio").GetSingle(),
        e.GetProperty("inverseMassTolerance").GetSingle(),e.GetProperty("inverseInertiaTolerance").GetSingle(),e.GetProperty("extentToleranceCm").GetSingle());
    private static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static void Require(bool b,string message){if(!b)throw new InvalidDataException(message);}
}

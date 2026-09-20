using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsBodyInertiaCompilerTests
{
    private static string Read(string name)=>File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+name+".json"));
    [Fact]
    public void BodyFormulaAndBoundsMatchNativeInternalParticlesForEveryTopology()
    {
        using var doc=JsonDocument.Parse(Read("v4_physics_inertia_reference"));var root=doc.RootElement;
        var settings=AlsBodyInertiaCompiler.Settings(root.GetProperty("settings"));
        Assert.True(settings.Enabled);Assert.Equal(20,settings.MaxDistanceCm);Assert.Equal(2.5f,settings.MaxRotationRatio);
        Assert.Equal(6,root.GetProperty("rigs").GetArrayLength());var count=0;var dynamic=0;var changed=0;
        var identities=new HashSet<(string,string)>();
        foreach(var rig in root.GetProperty("rigs").EnumerateArray())
        {
            Assert.True(identities.Add((rig.GetProperty("mesh").GetString()!,rig.GetProperty("isolatedChild").GetString()!)));
            Assert.Equal(rig.GetProperty("mesh").GetString()!.EndsWith("Mannequin.Mannequin")?19:21,rig.GetProperty("bodies").GetArrayLength());
            foreach(var body in rig.GetProperty("bodies").EnumerateArray())
            {
                var mass=Pose(body.GetProperty("massLocal"));
                var extents=AlsBodyInertiaConditioning.CollisionExtents(V(body.GetProperty("localBoundsMin")),V(body.GetProperty("localBoundsMax")),mass.Rotation);
                Assert.True((extents-V(body.GetProperty("collisionExtents"))).NearlyZero(1e-4),body.GetProperty("bone").GetString());
                foreach(var c in body.GetProperty("connectors").EnumerateArray())extents=AlsBodyInertiaConditioning.IncludeConnector(extents,V(c),mass);
                Assert.True((extents-V(body.GetProperty("constraintExtents"))).NearlyZero(1e-4));
                var isDynamic=body.GetProperty("dynamic").GetBoolean();
                var scale=isDynamic?AlsBodyInertiaConditioning.Calculate(body.GetProperty("inverseMass").GetSingle(),V(body.GetProperty("inverseInertia")).ToSingle(),extents.ToSingle(),settings):Vector3.One;
                Assert.InRange(Vector3.Distance(scale,V(body.GetProperty("utilityScale")).ToSingle()),0,1e-5f);
                Assert.InRange(Vector3.Distance(scale,V(body.GetProperty("actualScale")).ToSingle()),0,1e-5f);
                count++;if(isDynamic)dynamic++;if(scale!=Vector3.One)changed++;
            }
        }
        Assert.Equal(120,count);Assert.Equal(42,dynamic);Assert.True(changed>20);
    }
    [Fact]
    public void CompilerRecomputesFromBoundTopologyInsteadOfCopyingFullRigCorrection()
    {
        var json=Read("v4_physics_inertia_reference");using var doc=JsonDocument.Parse(json);var checkedBodies=0;
        foreach(var rig in doc.RootElement.GetProperty("rigs").EnumerateArray())
        {
            var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),rig.GetProperty("mesh").GetString()!);
            var settings=AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference"),definition);
            var child=rig.GetProperty("isolatedChild").GetString();
            if(!string.IsNullOrEmpty(child))
            {
                var joint=definition.Joints.Single(j=>definition.Bodies[j.ChildBody].Bone==child);
                settings=[settings[joint.Index] with{Index=0}];
                definition=definition with{Bodies=[definition.Bodies[joint.ParentBody] with{Index=0,PhysicsType=1},definition.Bodies[joint.ChildBody] with{Index=1}],
                    Joints=[joint with{Index=0,ParentBody=0,ChildBody=1}],DisabledCollisions=[(0,1)]};
            }
            var result=AlsBodyInertiaCompiler.Compile(json,definition,settings);
            foreach(var body in definition.Bodies)
            {
                var native=rig.GetProperty("bodies").EnumerateArray().Single(b=>b.GetProperty("bone").GetString()==body.Bone);
                Assert.True((result[body.Index].ExtentsCm-V(native.GetProperty("constraintExtents"))).NearlyZero(1e-4));
                Assert.InRange(Vector3.Distance(result[body.Index].InverseInertiaScale,V(native.GetProperty("actualScale")).ToSingle()),0,1e-5f);
                checkedBodies++;
            }
        }
        Assert.Equal(48,checkedBodies);
    }
    [Fact]
    public void MovingAConnectorChangesComputedInertiaWithoutChangingGeometry()
    {
        var json=Read("v4_physics_inertia_reference");var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"Mannequin.Mannequin");
        var settings=AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference"),definition);
        var original=AlsBodyInertiaCompiler.Compile(json,definition,settings);
        var joints=definition.Joints.ToArray();var index=Array.FindIndex(joints,j=>definition.Bodies[j.ChildBody].Bone=="spine_02");
        var joint=joints[index];joints[index]=joint with{ChildFrame=joint.ChildFrame with{Position=joint.ChildFrame.Position+new AlsDoubleVector(100,0,0)}};
        var changed=AlsBodyInertiaCompiler.Compile(json,definition with{Joints=joints},settings);
        Assert.True(Vector3.Distance(original[joint.ChildBody].InverseInertiaScale,changed[joint.ChildBody].InverseInertiaScale)>.1f);
        Assert.Equal(original[joint.ParentBody],changed[joint.ParentBody]);
    }
    [Fact]
    public void ReferenceRejectsWrongUnitsAssetAndMass()
    {
        var json=Read("v4_physics_inertia_reference");var definition=AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"),AlsPhysicsAssetCompiler.MeshRoot+"Mannequin.Mannequin");
        var settings=AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference"),definition);
        Assert.Throws<InvalidDataException>(()=>AlsBodyInertiaCompiler.Compile(json.Replace(AlsBodyInertiaCompiler.Coordinates,"meters"),definition,settings));
        Assert.Throws<InvalidDataException>(()=>AlsBodyInertiaCompiler.Compile(json,definition with{PhysicsAsset="/Game/Wrong"},settings));
        var bodies=definition.Bodies.ToArray();bodies[1]=bodies[1] with{MassKg=bodies[1].MassKg*2};
        Assert.Throws<InvalidDataException>(()=>AlsBodyInertiaCompiler.Compile(json,definition with{Bodies=bodies},settings));
    }
    private static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static AlsPrecisePose Pose(JsonElement e){var q=e.GetProperty("rotation");return new(V(e.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);}
}

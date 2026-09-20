using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsJointSolverReferenceTests
{
    private static JsonDocument Source() => JsonDocument.Parse(File.ReadAllText(
        AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_joint_solver_reference.json")));

    [Fact]
    public void RealSceneTrajectoriesHaveCompleteCoverageAndActualMotionAndForce()
    {
        using var doc=Source();var root=doc.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        Assert.StartsWith("5.9.0-",root.GetProperty("engine").GetString());
        Assert.Equal("UE world bone transforms; cm, kg, radians; force kg*cm/s^2, torque kg*cm^2/s^2",root.GetProperty("coordinates").GetString());
        Assert.Equal(12,root.GetProperty("stepsPerCase").GetInt32());
        var rows=root.GetProperty("cases");Assert.Equal(144,rows.GetArrayLength());
        var identities=new HashSet<(string,string,int,int,double,bool)>();
        foreach(var row in rows.EnumerateArray())
        {
            var mesh=row.GetProperty("mesh").GetString()!;var child=row.GetProperty("child").GetString()!;
            var hz=row.GetProperty("hz").GetInt32();var axis=row.GetProperty("axis").GetInt32();var angle=D(row,"perturbation");
            var fast=row.GetProperty("fullSpeedDrive").GetBoolean();
            Assert.Contains(mesh,new[]{AlsPhysicsAssetCompiler.MeshRoot+"Mannequin.Mannequin",AlsPhysicsAssetCompiler.MeshRoot+"AnimMan.AnimMan"});
            Assert.Contains(child,new[]{"spine_02","neck_01"});Assert.Contains(hz,new[]{30,60,120});Assert.InRange(axis,0,2);Assert.Contains(angle,new[]{-.6,.6});
            Assert.True(identities.Add((mesh,child,hz,axis,angle,fast)));
            Assert.Equal((double)(1f/hz),D(row,"dt"));
            var samples=row.GetProperty("samples");Assert.Equal(13,samples.GetArrayLength());
            var initialParent=Pose(samples[0].GetProperty("parent").GetProperty("world"));
            var initialChild=Pose(samples[0].GetProperty("child").GetProperty("world"));
            double maxMovement=0,maxReaction=0;var expectedFrame=0;
            foreach(var sample in samples.EnumerateArray())
            {
                var frame=sample.GetProperty("frame").GetInt32();Assert.InRange(frame,0,12);
                Assert.Equal(expectedFrame++,frame);
                Assert.InRange(System.Math.Abs(D(sample,"time")-frame*D(row,"dt")),0,1e-6);
                Assert.Equal(frame>0,sample.GetProperty("forceAvailable").GetBoolean());
                var parent=Pose(sample.GetProperty("parent").GetProperty("world"));
                var current=Pose(sample.GetProperty("child").GetProperty("world"));
                Assert.True((parent.Position-initialParent.Position).NearlyZero(1e-6));
                Assert.InRange(System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(parent.Rotation.Normalized(),initialParent.Rotation.Normalized()))),0,1e-12);
                var fp=AlsPrecisePose.Compose(Pose(row.GetProperty("parentFrame")),parent);
                var fc=AlsPrecisePose.Compose(Pose(row.GetProperty("childFrame")),current);
                var q=fc.Rotation;if(AlsQuaternion.Dot(fp.Rotation,q)<0)q=q*-1;
                var calculated=AlsJointAngularKinematics.Evaluate(fp.Rotation.Normalized(),q.Normalized()).Angles;
                Assert.True((calculated-V(sample.GetProperty("angles"))).NearlyZero(1e-6));
                Assert.True((fc.Position-fp.Position).LengthSquared<25,"Native pair separated over 5 cm.");
                foreach(var name in new[]{"parent","child"})
                {
                    Assert.True(V(sample.GetProperty(name).GetProperty("linearVelocity")).IsFinite);
                    Assert.True(V(sample.GetProperty(name).GetProperty("angularVelocity")).IsFinite);
                }
                var force=V(sample.GetProperty("force"));var torque=V(sample.GetProperty("torque"));
                Assert.True(force.IsFinite&&torque.IsFinite);
                maxReaction=System.Math.Max(maxReaction,force.LengthSquared+torque.LengthSquared);
                maxMovement=System.Math.Max(maxMovement,(current.Position-initialChild.Position).LengthSquared+
                    1-System.Math.Abs(AlsQuaternion.Dot(current.Rotation,initialChild.Rotation)));
            }
            Assert.True(maxMovement>1e-8,"Time advanced but body did not move.");
            Assert.True(maxReaction>1e-8,"No actual native joint force was observed.");
        }
    }

    [Fact]
    public void LiveSolverOverridesAndBothInertiaStagesAreObserved()
    {
        using var doc=Source();var rows=doc.RootElement.GetProperty("cases");var changed=0;
        foreach(var row in rows.EnumerateArray())
        {
            var s=row.GetProperty("solverSettings");
            Assert.Equal((double).2f,D(s,"MinParentMassRatio"));Assert.Equal(5,D(s,"MaxInertiaRatio"));
            Assert.Equal((double).025f,D(s,"PositionTolerance"));Assert.Equal((double).001f,D(s,"AngleTolerance"));
            Assert.True(s.GetProperty("bUseSimd").GetBoolean());Assert.True(s.GetProperty("bSolvePositionLast").GetBoolean());
            Assert.True(s.GetProperty("bUsePositionBasedDrives").GetBoolean());
            Assert.True(row.GetProperty("positionIterations").GetInt32()>0);
            var bodies=row.GetProperty("bodies");Assert.Equal(2,bodies.GetArrayLength());
            Assert.Equal(AlsDoubleVector.Zero,V(bodies[0].GetProperty("bodyConditionedInverseInertia")));
            var b=bodies[1];var scale=V(b.GetProperty("bodyInverseInertiaScale"));var raw=V(b.GetProperty("inertiaKgCm2"));
            var effective=V(b.GetProperty("bodyConditionedInverseInertia"));
            for(var axis=0;axis<3;axis++)
            {
                Assert.InRange(scale[axis],double.Epsilon,1);Assert.True(raw[axis]>0);
                Assert.InRange(System.Math.Abs(effective[axis]-scale[axis]/raw[axis]),0,1e-8);
                if(scale[axis]<.99)changed++;
            }
        }
        Assert.True(changed>0,"Raw asset inertia was mistaken for effective physics inertia.");
    }

    [Fact]
    public void TrajectoryPairsRemainBoundToTheExportedAssetFramesMassAndDrive()
    {
        using var doc=Source();
        var assetJson=File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_asset_inputs.json"));
        var settingsJson=File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_joint_reference.json"));
        var rigs=new[]{"Mannequin","AnimMan"}.Select(name=>AlsPhysicsAssetCompiler.Compile(assetJson,AlsPhysicsAssetCompiler.MeshRoot+name+"."+name))
            .ToDictionary(r=>r.Mesh,r=>(Rig:r,Settings:AlsPhysicsJointCompiler.Compile(settingsJson,r)));
        foreach(var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var (rig,settings)=rigs[row.GetProperty("mesh").GetString()!];
            Assert.Equal(rig.PhysicsAsset,row.GetProperty("physicsAsset").GetString());
            var joint=rig.Joints.Single(j=>rig.Bodies[j.ChildBody].Bone==row.GetProperty("child").GetString());
            Assert.Equal(rig.Bodies[joint.ParentBody].Bone,row.GetProperty("parent").GetString());
            Assert.True((joint.ParentFrame.Position-Pose(row.GetProperty("parentFrame")).Position).NearlyZero(1e-8));
            Assert.True((joint.ChildFrame.Position-Pose(row.GetProperty("childFrame")).Position).NearlyZero(1e-8));
            var sourceBodies=new[]{rig.Bodies[joint.ParentBody],rig.Bodies[joint.ChildBody]};
            for(var i=0;i<2;i++)
            {
                var body=row.GetProperty("bodies")[i];
                Assert.Equal(sourceBodies[i].MassKg,D(body,"massKg"));
                Assert.Equal(sourceBodies[i].InertiaKgCm2,V(body.GetProperty("inertiaKgCm2")));
            }
            var native=row.GetProperty("jointSettings");var baseline=settings[joint.Index];
            Assert.True(native.GetProperty("bUseLinearSolver").GetBoolean());
            Assert.Equal(baseline.AngularLimitsRad,V(native.GetProperty("AngularLimits")));
            var fast=row.GetProperty("fullSpeedDrive").GetBoolean();
            Assert.Equal(fast?new(37500,37500,37500):baseline.AngularDrive.Stiffness,V(native.GetProperty("AngularDriveStiffness")));
            Assert.Equal(fast?AlsDoubleVector.Zero:baseline.AngularDrive.Damping,V(native.GetProperty("AngularDriveDamping")));
        }
    }

    [Fact]
    public void JointMassConditionerMatchesNativeUtilityIncludingActiveBranches()
    {
        using var doc=Source();var root=doc.RootElement;var count=0;
        foreach(var row in root.GetProperty("conditioningUtilityCases").EnumerateArray()){Check(row);count++;}
        Assert.Equal(8,count);
        foreach(var c in root.GetProperty("cases").EnumerateArray())
        foreach(var row in c.GetProperty("jointConditioningMath").EnumerateArray()){Check(row);count++;}
        Assert.Equal(152,count);
        static void Check(JsonElement row)
        {
            var output=AlsJointMassConditioning.Apply(new(D(row,"parentInverseMass"),V(row.GetProperty("parentInverseInertia"))),
                new(D(row,"childInverseMass"),V(row.GetProperty("childInverseInertia"))),D(row,"minParentMassRatio"),D(row,"maxInertiaRatio"));
            Assert.InRange(System.Math.Abs(output.Parent.Mass-D(row,"resultParentInverseMass")),0,1e-12);
            Assert.InRange(System.Math.Abs(output.Child.Mass-D(row,"resultChildInverseMass")),0,1e-12);
            Assert.True((output.Parent.Inertia-V(row.GetProperty("resultParentInverseInertia"))).NearlyZero(1e-12));
            Assert.True((output.Child.Inertia-V(row.GetProperty("resultChildInverseInertia"))).NearlyZero(1e-12));
        }
    }
    private static double D(JsonElement e,string name)=>e.GetProperty(name).GetDouble();
    private static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static AlsPrecisePose Pose(JsonElement e)
    {
        var q=e.GetProperty("rotation");var p=new AlsPrecisePose(V(e.GetProperty("position")),new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),AlsDoubleVector.One);
        p.Validate(1e-6);return p;
    }
}

using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsNativeMotorPoseTests
{
    [Theory] [InlineData("Mannequin")] [InlineData("AnimMan")]
    public void ActualSkeletonMappingConvertsFbxLocalsAndRejectsPartialPublication(string mesh)
    {
        var definition=AlsPhysicsAssetCompiler.Compile(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_asset_inputs.json")),AlsPhysicsAssetCompiler.MeshRoot+mesh+"."+mesh);
        // Insert an unused virtual leaf after root: all subsequent real indices
        // shift, so matching row numbers cannot accidentally satisfy this test.
        var names=definition.Bones.Select(b=>b.Name).ToList(); names.Insert(1,"TestVirtual");
        var parents=definition.Bones.Select(b=>b.Parent<1?b.Parent:b.Parent+1).ToList(); parents.Insert(1,0);
        var adapter=new AlsNativeMotorPose(definition,names.ToArray(),parents.ToArray());
        var local=names.Select(_=>AlsLocalPose.Identity).ToArray();
        for(var i=0;i<definition.Bones.Length;i++)
        {
            var p=definition.Bones[i].Local; var q=p.Rotation;
            local[i==0?0:i+1]=new(new((float)(p.Position.X*.01),(float)(-p.Position.Y*.01),(float)(p.Position.Z*.01)),
                new((float)-q.X,(float)q.Y,(float)-q.Z,(float)q.W),p.Scale.ToSingle());
        }
        var native=new AlsPrecisePose[definition.Bones.Length]; adapter.Convert(local,native);
        for(var i=0;i<native.Length;i++)
        {
            var delta=native[i].Position-definition.Bones[i].Local.Position;
            Assert.InRange(Math.Sqrt(AlsDoubleVector.Dot(delta,delta)),0,1e-4);
            Assert.InRange(Math.Abs(AlsQuaternion.Dot(native[i].Rotation,definition.Bones[i].Local.Rotation)-1),0,1e-6);
        }
        var saved=native.ToArray(); var last=local[^1];
        local[^1]=last with { Position=new(float.NaN,0,0) };
        Assert.Throws<ArgumentException>(()=>adapter.Convert(local,native)); Assert.Equal(saved,native);
        local[^1]=last;
        for(var i=0;i<256;i++) adapter.Convert(local,native);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<2048;i++) adapter.Convert(local,native);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        parents[^1]=-1;
        Assert.Throws<ArgumentException>(()=>new AlsNativeMotorPose(definition,names.ToArray(),parents.ToArray()));
    }
}

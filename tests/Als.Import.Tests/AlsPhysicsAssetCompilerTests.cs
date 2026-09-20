using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsAssetCompilerTests
{
    private static string Source() => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_asset_inputs.json"));
    private static string Mesh(string name) => AlsPhysicsAssetCompiler.MeshRoot + name + "." + name;

    [Theory]
    [InlineData("Mannequin",19,18,21,82.17038989067078)]
    [InlineData("AnimMan",21,20,22,81.48245322704315)]
    public void NativeBodiesBindAndRetainMassShapesJointFramesAndDrivers(string name,int bodies,int joints,int shapes,double mass)
    {
        var result = AlsPhysicsAssetCompiler.Compile(Source(),Mesh(name));
        Assert.EndsWith(name + "_PhysicsAsset." + name + "_PhysicsAsset", result.PhysicsAsset);
        Assert.Equal(bodies,result.Bodies.Length); Assert.Equal(joints,result.Joints.Length);
        Assert.Equal(shapes,result.Bodies.Sum(b => b.Shapes.Length));
        Assert.InRange(Math.Abs(result.Bodies.Sum(b => b.MassKg)-mass),0,1e-6);
        var components = new AlsPrecisePose[result.Bones.Length];
        for (var i = 0; i < components.Length; i++) components[i] = result.Bones[i].Parent < 0 ? result.Bones[i].Local :
            AlsPrecisePose.Compose(result.Bones[i].Local,components[result.Bones[i].Parent]);
        foreach (var body in result.Bodies)
        {
            // Independent UE-created rigid bodies must coincide with composed skeletal reference transforms.
            Assert.InRange((body.ReferenceComponent.Position-components[body.BoneIndex].Position).LengthSquared,0,1e-6);
            Assert.InRange(1-Math.Abs(AlsQuaternion.Dot(body.ReferenceComponent.Rotation,components[body.BoneIndex].Rotation)),-1e-6,1e-6);
            Assert.True(body.InertiaKgCm2.X > 0 && body.InertiaKgCm2.Y > 0 && body.InertiaKgCm2.Z > 0);
            Assert.True(body.Defaults.GetProperty("bEnableGravity").GetBoolean());
        }
        Assert.Contains(result.Bodies,b => b.Shapes.Length > 1);
        Assert.Contains(result.Joints,j => j.NativeInstance.GetProperty("profileInstance").GetProperty("coneLimit").GetProperty("bSoftConstraint").GetBoolean());
        Assert.All(result.Joints,j => Assert.True(j.NativeInstance.GetProperty("profileInstance").TryGetProperty("angularDrive",out _)));
        // FBX display casing and skeleton order are not native body index order.
        var reversed = result.Bones.Select(b => b.Name.ToUpperInvariant()).Reverse().ToArray();
        var binding = result.Bind(reversed);
        for (var i = 0; i < binding.Length; i++) Assert.Equal(result.Bodies[i].Bone.ToUpperInvariant(),reversed[binding[i]]);
        Assert.Throws<InvalidDataException>(() => result.Bind(reversed.Where(n => n != "PELVIS").ToArray()));
        Assert.Throws<InvalidDataException>(() => result.Bind([..reversed,"PeLvIs"]));
    }

    [Fact]
    public void OriginalCharacterBindingDoesNotSilentlyReplaceTheCurrentGodotMesh()
    {
        using var doc = JsonDocument.Parse(Source()); var binding = doc.RootElement.GetProperty("characterBinding");
        Assert.Equal(Mesh("AnimMan"),binding.GetProperty("mesh").GetString());
        var current = AlsPhysicsAssetCompiler.Compile(Source(),Mesh("Mannequin"));
        Assert.NotEqual(current.PhysicsAsset,binding.GetProperty("effectivePhysicsAsset").GetString());
        var anim = AlsPhysicsAssetCompiler.Compile(Source(),Mesh("AnimMan"));
        Assert.Equal(2,anim.Bodies.SelectMany(b => b.Shapes).Count(s => s.Type == "convex"));
        Assert.All(anim.Bodies.SelectMany(b => b.Shapes).Where(s => s.Type == "convex"),s => Assert.True(s.VerticesCm.Length >= 4));
        Assert.All(anim.Bodies.SelectMany(b => b.Shapes).Where(s => s.Type == "convex"),s =>
            Assert.Equal(new AlsDoubleVector(1,1.2249667644500732,1),s.Local.Scale));
    }

    [Theory]
    [InlineData("schema")] [InlineData("units")] [InlineData("mass")] [InlineData("inertia")]
    [InlineData("geometry")] [InlineData("type")] [InlineData("scale")] [InlineData("body")]
    [InlineData("parent")] [InlineData("cycle")] [InlineData("exclusion")] [InlineData("jointProfile")]
    [InlineData("convexIndex")] [InlineData("duplicateMesh")]
    public void BrokenOrIncompletePhysicsDataIsRejectedBeforeCreatingBodies(string mutation)
    {
        var root = JsonNode.Parse(Source())!; var row = root["meshes"]![mutation == "convexIndex" ? 1 : 0]!;
        var body = row["bodies"]![0]!;
        switch (mutation)
        {
            case "schema": root["schemaVersion"] = 2; break;
            case "units": root["coordinates"] = "meters"; break;
            case "mass": body["nativeMassKg"] = 0; break;
            case "inertia": body["nativeInertiaKgCm2"]![0] = -1; break;
            case "geometry": body["shapes"] = new JsonArray(); break;
            case "type": body["shapes"]![0]!["type"] = "levelSet"; break;
            case "scale": body["shapes"]![0]!["local"]!["scale"]![0] = 2; break;
            case "body": body["bone"] = "missing"; break;
            case "parent": row["constraints"]![0]!["parentBone"] = "missing"; break;
            case "cycle":
                var child = row["constraints"]![0]!["childBone"]!.GetValue<string>();
                row["constraints"]![0]!["parentBone"] = child; break;
            case "exclusion": row["collisionDisableTable"]![0]!["b"] = 999; break;
            case "jointProfile": row["constraints"]![0]!["nativeInstance"]!["profileInstance"]!["angularDrive"] = 1; break;
            case "convexIndex":
                var convex = row["bodies"]!.AsArray().SelectMany(b => b!["shapes"]!.AsArray()).First(s => s!["type"]!.GetValue<string>() == "convex")!;
                convex["indices"]![0] = 99999; break;
            case "duplicateMesh": root["meshes"]!.AsArray().Add(row.DeepClone()); break;
        }
        Assert.Throws<InvalidDataException>(() => AlsPhysicsAssetCompiler.Compile(root.ToJsonString(),Mesh(mutation == "convexIndex" ? "AnimMan" : "Mannequin")));
    }
}

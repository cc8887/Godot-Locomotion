using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredBlendPoseTests(ITestOutputHelper output)
{
    [Fact]
    public void AllEightSpacesMatchCompleteNativeBlendsWithoutReferenceDrivenWeights()
    {
        var catalogJson=MantlingHostFixture.Read("refactored_animation_sources");var inputs=MantlingHostFixture.Read("refactored_triangulation_inputs");
        byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
        var catalog=new AlsRefactoredAnimationCatalog(catalogJson,Read);
        var profiles=AlsRefactoredTriangulationCompiler.Compile(inputs,catalogJson,Read);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_blend_pose_reference"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs))).ToLowerInvariant(),doc.RootElement.GetProperty("inputsSha256").GetString());
        var poses=0;var curveValues=0;double maxP=0,maxQ=0,maxS=0;float maxCurve=0;
        foreach(var space in doc.RootElement.GetProperty("spaces").EnumerateArray())
        {
            var path=space.GetProperty("source").GetString()!;var profile=profiles[path];var source=new AlsRefactoredBlendPoseSource(profile,catalog);
            Assert.Equal(path.Contains("/Lean/"),source.IsAdditive);
            var sampler=source.CreateSampler();var independent=source.CreateSampler();
            var pose=new AlsPrecisePose[source.BoneNames.Length];var curves=new AlsInertialCurve[source.CurveNames.Length];
            var repeat=new AlsPrecisePose[pose.Length];var repeatedCurves=new AlsInertialCurve[curves.Length];
            foreach(var reference in space.GetProperty("poses").EnumerateArray())
            {
                var point=new AlsBlendPoint(reference.GetProperty("pitch").GetSingle(),reference.GetProperty("y").GetSingle());
                var time=reference.GetProperty("normalizedTime").GetSingle();
                var cache=sampler.Evaluate(point,time,-1,pose,curves);
                Assert.Equal(source.BoneNames.ToArray(),reference.GetProperty("names").EnumerateArray().Select(v=>v.GetString()!));
                for(var bone=0;bone<pose.Length;bone++)
                {
                    var expected=reference.GetProperty("pose")[bone];
                    double[] V(string name)=>expected.GetProperty(name).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                    var p=V("position");var q=V("rotation");var s=V("scale");var a=pose[bone];
                    var dp=Math.Sqrt(Math.Pow(a.Position.X-p[0],2)+Math.Pow(a.Position.Y-p[1],2)+Math.Pow(a.Position.Z-p[2],2));
                    var sign=a.Rotation.X*q[0]+a.Rotation.Y*q[1]+a.Rotation.Z*q[2]+a.Rotation.W*q[3]<0?-1:1;
                    var dq=new[]{Math.Abs(a.Rotation.X-sign*q[0]),Math.Abs(a.Rotation.Y-sign*q[1]),Math.Abs(a.Rotation.Z-sign*q[2]),Math.Abs(a.Rotation.W-sign*q[3])}.Max();
                    var ds=new[]{Math.Abs(a.Scale.X-s[0]),Math.Abs(a.Scale.Y-s[1]),Math.Abs(a.Scale.Z-s[2])}.Max();
                    maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                    Assert.True(dp<=2e-5&&dq<=1e-6&&ds<=1e-6,$"{path} xy={point} t={time:R} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
                }
                var native=reference.GetProperty("curves");var present=0;
                for(var i=0;i<curves.Length;i++)
                {
                    Assert.Equal(native.TryGetProperty(source.CurveNames[i],out var expected),curves[i].Present);
                    if(!curves[i].Present)continue;
                    var delta=MathF.Abs(curves[i].Value-expected.GetSingle());maxCurve=MathF.Max(maxCurve,delta);present++;curveValues++;
                    Assert.True(delta<=1e-6f,$"{path} xy={point} t={time:R} curve={source.CurveNames[i]} delta={delta:R}");
                }
                Assert.Equal(native.EnumerateObject().Count(),present);
                pose.CopyTo(repeat,0);curves.CopyTo(repeatedCurves,0);
                Assert.Throws<ArgumentException>(()=>sampler.Evaluate(new(double.NaN,0),time,cache,pose,curves));
                Assert.Equal(repeat,pose);Assert.Equal(repeatedCurves,curves);
                Parallel.Invoke(()=>Assert.Equal(cache,sampler.Evaluate(point,time,-1,pose,curves)),
                    ()=>Assert.Equal(cache,independent.Evaluate(point,time,-1,repeat,repeatedCurves)));
                Assert.Equal(repeat,pose);Assert.Equal(repeatedCurves,curves);poses++;
            }
            var foreign=new AlsRefactoredAnimationCatalog(catalogJson+" ",Read);
            Assert.Throws<ArgumentException>(()=>new AlsRefactoredBlendPoseSource(profile,foreign));
        }
        Assert.Equal(400,poses);output.WriteLine($"poses={poses} curveValues={curveValues} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxCurve={maxCurve:R}");
    }
}

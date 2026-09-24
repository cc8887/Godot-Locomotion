using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredAdditiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("basePoseType")][InlineData("forceRootLock")][InlineData("enableRootMotion")][InlineData("transformCurveCount")]
    public void RejectsUnsupportedPoliciesEvenWithValidPayloadDigest(string field)
    {
        var index=JsonNode.Parse(MantlingHostFixture.Read("refactored_animation_sources"))!;
        var entry=index["assets"]!.AsArray().First(a=>a!["source"]!.GetValue<string>().Contains("A_Als_Land_Heavy_Additive."))!;
        var file=entry["file"]!.GetValue<string>();
        byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
        var payload=JsonNode.Parse(Read(file))!;var policy=payload["evaluation"]!;
        if(field=="basePoseType")policy[field]="ABPT_AnimScaled";
        else if(field=="transformCurveCount")policy[field]=1;
        else policy[field]=true;
        var bytes=Encoding.UTF8.GetBytes(payload.ToJsonString());entry["sha256"]=Convert.ToHexString(SHA256.HashData(bytes));
        var catalog=new AlsRefactoredAnimationCatalog(index.ToJsonString(),p=>p==file?bytes:Read(p));
        Assert.Throws<ArgumentException>(()=>catalog.CompileAdditivePose(entry["source"]!.GetValue<string>()));
    }
    [Fact]
    public void EveryOriginalAdditiveMatchesNativePoseAndCurvesAndIndependentOwners()
    {
        var index=MantlingHostFixture.Read("refactored_animation_sources");
        var catalog=new AlsRefactoredAnimationCatalog(index,p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_additive_reference"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(index))).ToLowerInvariant(),doc.RootElement.GetProperty("catalogSha256").GetString());
        var rows=doc.RootElement.GetProperty("sequences");Assert.Equal(42,rows.GetArrayLength());
        double maxP=0,maxQ=0,maxS=0;float maxCurve=0;var poses=0;var curveValues=0;
        foreach(var row in rows.EnumerateArray())
        {
            var path=row.GetProperty("source").GetString()!;var source=catalog.CompileAdditivePose(path);
            var sampler=source.CreateSampler();var other=source.CreateSampler();
            var pose=new AlsPrecisePose[source.BoneNames.Length];var curves=new AlsInertialCurve[source.CurveNames.Length];
            var repeat=new AlsPrecisePose[pose.Length];var repeatCurves=new AlsInertialCurve[curves.Length];
            var references=row.GetProperty("poses").EnumerateArray().ToArray();Assert.Equal(5,references.Length);
            foreach(var reference in references)
            {
                var time=reference.GetProperty("timeSeconds").GetDouble();sampler.Sample(time,pose,curves);
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
                    Assert.True(dp<=1e-6&&dq<=1e-8&&ds<=1e-8,$"{path} t={time:R} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
                }
                var nativeCurves=reference.GetProperty("curves");
                Assert.Equal(curves.Length,nativeCurves.EnumerateObject().Count());
                for(var i=0;i<curves.Length;i++)
                {
                    Assert.True(curves[i].Present);var delta=MathF.Abs(curves[i].Value-nativeCurves.GetProperty(source.CurveNames[i]).GetSingle());
                    maxCurve=MathF.Max(maxCurve,delta);Assert.True(delta<=1e-6f,$"{path} t={time:R} curve={source.CurveNames[i]} delta={delta:R}");curveValues++;
                }
                // A failed request must leave the last output unchanged and be retryable.
                pose.CopyTo(repeat,0);curves.CopyTo(repeatCurves,0);
                Assert.Throws<ArgumentException>(()=>sampler.Sample(double.NaN,pose,curves));
                Assert.Equal(repeat,pose);Assert.Equal(repeatCurves,curves);
                sampler.Sample(time,pose,curves);Assert.Equal(repeat,pose);Assert.Equal(repeatCurves,curves);poses++;
            }
            foreach(var reference in references.Reverse())
            {
                var time=reference.GetProperty("timeSeconds").GetDouble();
                Parallel.Invoke(()=>sampler.Sample(time,pose,curves),()=>other.Sample(time,repeat,repeatCurves));
                Assert.Equal(pose,repeat);Assert.Equal(curves,repeatCurves);
            }
        }
        Assert.Equal(210,poses);
        output.WriteLine($"poses={poses} curves={curveValues} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxCurve={maxCurve:R}");
    }
}

using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingMotionTests(ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/"+name+".json"));
    private static AlsMantlingMotionProfile Profile()=>AlsMantlingMotionCompiler.Compile(Read("refactored_mantle_inputs"),
        Read("refactored_mantle_root_tracks"),Read("refactored_mantle_blends"));
    private static float F(JsonNode row,string key)=>row[key]!.GetValue<float>();
    private static AlsPrecisePose Pose(JsonNode row)
    {
        double[] V(string key)=>row[key]!.AsArray().Select(x=>x!.GetValue<double>()).ToArray();
        var p=V("position");var q=V("rotation");var s=V("scale");
        return new(new(p[0],p[1],p[2]),new(q[0],q[1],q[2],q[3]),new(s[0],s[1],s[2]));
    }
    [Fact]
    public void BoundMotionReplaysNativeSourceUsingItsOwnActorAndClockHistory()
    {
        var profile=Profile();var reference=JsonNode.Parse(Read("refactored_mantle_motion_reference"))!;
        foreach(var pair in new[]{("settingsSha256","refactored_mantle_inputs"),("rootsSha256","refactored_mantle_root_tracks"),("blendsSha256","refactored_mantle_blends")})
            Assert.Equal(reference[pair.Item1]!.GetValue<string>(),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Read(pair.Item2)))).ToLowerInvariant());
        var count=0;var clear=0;double maxPosition=0,maxVelocity=0,maxRotation=0,maxActorRotation=0,maxAnchor=0,maxSeek=0;
        foreach(var item in reference["traces"]!.AsArray())
        {
            var trace=item!;var definition=profile.Definitions[trace["settings"]!.GetValue<string>()];
            var setting=definition.Settings;
            var settings=definition.CreateWarpSettings((setting.ReferenceLow+setting.ReferenceHigh)*.5f,30);
            Assert.Equal(F(trace,"startTime"),settings.MontageStartTime);Assert.Equal(F(trace,"duration"),settings.Duration);
            Assert.Equal(F(trace,"blendInTime"),settings.BlendInTime);Assert.False(trace["customBlend"]!.GetValue<bool>());
            Assert.Equal(settings.BlendInOption.ToString(),trace["blendInOption"]!.GetValue<string>());
            var actor=Pose(trace["actor"]!);var mesh=Pose(trace["mesh"]!).Rotation;var relative=trace["relative"]!.GetValue<bool>();
            var anchors=AlsMantlingRootMotion.CreateAnchors(actor,Pose(trace["desired"]!),mesh,
                definition.Roots.Sample(settings.MontageStartTime),definition.Roots.SampleLast(),relative,Pose(trace["base"]!));
            foreach(var pair in new[]{(anchors.Start,Pose(trace["startAnchor"]!)),(anchors.Target,Pose(trace["targetAnchor"]!))})
            {
                var error=System.Math.Sqrt((pair.Item1.Position-pair.Item2.Position).LengthSquared);
                maxAnchor=System.Math.Max(maxAnchor,error);Assert.True(error<=.001,$"Anchor error {error}");
                Assert.True(System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(pair.Item1.Rotation,pair.Item2.Rotation)))<=1e-7);
            }
            float time=0,seek=0;
            foreach(var row in trace["frames"]!.AsArray())
            {
                var frame=row!;var delta=F(frame,"delta");
                var step=AlsMantlingRootMotion.Prepare(time,F(frame,"simulationDelta"),delta,settings,anchors,actor,mesh,
                    new(0,0,1),frame["targetExists"]!.GetValue<bool>(),relative,Pose(frame["base"]!),definition.Roots);
                time=step.Time;Assert.Equal(F(frame,"time"),time);
                Assert.Equal(frame["hasMotion"]!.GetValue<bool>(),step.HasRootMotion);
                if(step.HasRootMotion)
                {
                    seek=step.MontagePosition;
                    var motion=Pose(frame["motion"]!);
                    var velocity=System.Math.Sqrt((step.Velocity-motion.Position).LengthSquared);
                    maxVelocity=System.Math.Max(maxVelocity,velocity);Assert.True(velocity<=.01,$"Velocity error {velocity} at {count}");
                    var rotation=System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(step.RotationDelta,motion.Rotation)));
                    maxRotation=System.Math.Max(maxRotation,rotation);Assert.True(rotation<=1e-7,$"Rotation error {rotation} frame={count} time={time} actorNorm={actor.Rotation.LengthSquared:G17} deltaNorm={step.RotationDelta.LengthSquared:G17} nativeDeltaNorm={motion.Rotation.LengthSquared:G17}");
                    // SceneComponent writes rotation through its Rotator cache and
                    // regenerates a unit quaternion. Do not feed norm drift back
                    // into the source's unit-quaternion inverse on the next frame.
                    actor=new(actor.Position+step.Velocity*delta,(step.RotationDelta*actor.Rotation).Normalized(),actor.Scale);
                }
                else clear++;
                var position=System.Math.Sqrt((actor.Position-Pose(frame["actor"]!).Position).LengthSquared);
                maxPosition=System.Math.Max(maxPosition,position);Assert.True(position<=.001,$"Actor error {position}");
                var actorRotation=System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(actor.Rotation,Pose(frame["actor"]!).Rotation)));
                maxActorRotation=System.Math.Max(maxActorRotation,actorRotation);Assert.True(actorRotation<=1e-7,$"Actor rotation error {actorRotation}");
                var seekError=System.Math.Abs(seek-F(frame,"montagePosition"));maxSeek=System.Math.Max(maxSeek,seekError);
                Assert.True(seekError<=1e-6,$"Seek error {seekError}");count++;
            }
        }
        Assert.Equal(4536,count);Assert.Equal(126,clear);
        output.WriteLine($"frames={count} cleared={clear} anchor_cm={maxAnchor:G17} actor_cm={maxPosition:G17} velocity_cm_s={maxVelocity:G17} rotation_dot={maxRotation:G17} actor_rotation_dot={maxActorRotation:G17} seek_s={maxSeek:G17}");
    }
    [Theory]
    [InlineData("missing")][InlineData("duplicate")][InlineData("option")][InlineData("negative")]
    public void InvalidBlendClosureCannotPublishMotion(string fault)
    {
        var source=JsonNode.Parse(Read("refactored_mantle_blends"))!;var rows=source["montages"]!.AsArray();
        switch(fault)
        {
            case "missing": rows.RemoveAt(0);break;
            case "duplicate": rows.Add(rows[0]!.DeepClone());break;
            case "option": rows[0]!["option"]="CUSTOM";break;
            case "negative": rows[0]!["time"]=-1;break;
        }
        Assert.Throws<ArgumentException>(()=>AlsMantlingMotionCompiler.Compile(Read("refactored_mantle_inputs"),
            Read("refactored_mantle_root_tracks"),source.ToJsonString()));
    }
}

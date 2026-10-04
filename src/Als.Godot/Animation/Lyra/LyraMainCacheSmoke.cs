using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainCacheSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Exact(float a,float b,string label)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),label+$" {a:R}/{b:R}");
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Main cache failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_cache_v1_requests.json"));
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"main_cache_v1_native.json"));
        var data=native.RootElement;
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"main_cache_v1_requests.json")),"Stale cache request");
        Require(data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"main_cache_v1_policy.json")),"Stale cache policy");
        var graphs=LyraMainLayerGraphCatalog.Load();var frames=0;var updates=0;var skipped=0;var active=0;var rootZero=0;var empty=0;var rejected=0;
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var traversal=new LyraMainPoseCacheTraversal(graphs,trace.GetProperty("profile").GetString()!);
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var identity=new AlsFrameIdentity(i,17,7);
                var readers=new List<LyraMainCacheUpdate>();
                if(frame.GetProperty("visited").GetBoolean())foreach(var r in frame.GetProperty("readers").EnumerateArray())
                {
                    var context=new AlsPoseUpdateContext(identity,r.GetProperty("weight").GetSingle(),frame.GetProperty("delta").GetSingle(),
                        r.GetProperty("rootMotionWeight").GetSingle(),r.GetProperty("shared").GetBoolean()).WithInertialization(75,true);
                    if(!r.GetProperty("active").GetBoolean())context=context.AsInactive();
                    readers.Add(new(r.GetProperty("reader").GetInt32(),context));
                }
                var factors=new LyraMainCacheSourceWeights(frame.GetProperty("preAimSource").GetSingle(),frame.GetProperty("upperSource").GetSingle(),
                    frame.GetProperty("preAimInactive").GetBoolean(),frame.GetProperty("upperInactive").GetBoolean());
                var result=traversal.Resolve(identity,readers.ToArray(),factors);var label=$"Cache/{ti}/{i}";
                Require(result.Updates.Length==row.GetProperty("updates").GetArrayLength()&&result.Skipped.Length==row.GetProperty("skipped").GetArrayLength(),label+"/dispatch count");
                for(var n=0;n<result.Updates.Length;n++)
                {
                    var actual=result.Updates[n];var expected=row.GetProperty("updates")[n];var c=actual.Context;
                    Require(actual.Cache==expected.GetProperty("cache").GetInt32()&&c.IsActive==expected.GetProperty("active").GetBoolean()&&c.HasSharedContext==expected.GetProperty("shared").GetBoolean(),label+"/ordered complete context");
                    Exact(c.Weight,expected.GetProperty("weight").GetSingle(),label+"/weight");Exact(c.RootMotionWeight,expected.GetProperty("rootMotionWeight").GetSingle(),label+"/root modifier");
                    updates++;active+=c.IsActive?1:0;rootZero+=c.RootMotionWeight==0?1:0;
                }
                for(var n=0;n<result.Skipped.Length;n++)
                {
                    var s=result.Skipped[n];var expected=row.GetProperty("skipped")[n];
                    Require(s.Cache==expected.GetProperty("cache").GetInt32()&&s.Handler==75&&s.Contexts.Length==expected.GetProperty("count").GetInt32(),label+"/skipped forwarding");skipped+=s.Contexts.Length;
                }
                for(var n=0;n<3;n++)Exact(result.Updates.FirstOrDefault(u=>u.Cache==new[]{181,78,83}[n]).Context.Weight,row.GetProperty("globalWeights")[n].GetSingle(),label+"/empty cache resets global weight");
                // Late cancellation discards the result; rebuilding the same
                // character candidate must reproduce every path and message.
                Require(JsonSerializer.Serialize(result)==JsonSerializer.Serialize(traversal.Resolve(identity,readers.ToArray(),factors)),label+"/retry");
                if(i%47==0)
                {
                    try{traversal.Resolve(identity,[new(76,new AlsPoseUpdateContext(new(i,18,7),1,.01f))],factors);throw new Exception("Accepted foreign cache context.");}
                    catch(InvalidOperationException){rejected++;}
                    Require(JsonSerializer.Serialize(result)==JsonSerializer.Serialize(traversal.Resolve(identity,readers.ToArray(),factors)),label+"/failed candidate recovery");
                }
                empty+=result.Updates.Length==0?1:0;frames++;
            }
        }
        Require(frames==2520&&updates==data.GetProperty("counts").GetProperty("updates").GetInt32()&&skipped==data.GetProperty("counts").GetProperty("skipped").GetInt32()&&active>0&&rootZero>0&&empty>0&&rejected>0,"Incomplete cache coverage");
        GD.Print($"LYRA_MAIN_CACHE_GODOT_OK frames={frames} updates={updates} skipped={skipped} active={active} rootZero={rootZero} empty={empty} rejected={rejected} exact=true retry=true queue=181,78,83 poseEvaluation=false production=false");
    }
}

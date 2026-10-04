using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraCacheOwnerSmoke:Node
{
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private sealed class UpdateSink(Action<int,float> record,Action<int,float> source):IAlsPoseCacheUpdateSink
    {
        public void UpdateCachedSource(int node,in AlsPoseUpdateContext context){record(node,context.Weight);source(node,context.Weight);}
        public void OnCachedUpdatesSkipped(int node,ReadOnlySpan<AlsPoseUpdateContext> contexts){}
    }
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Cache owner failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        using var resources=new LyraLocomotionResources();var bank=resources.Catalog.Bank;
        using var requests=JsonDocument.Parse(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/cache-owner-v2-requests.json")));
        using var native=JsonDocument.Parse(System.IO.File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/cache-owner-v2-native.json")));
        var rows=0;var fields=0;var evaluations=0;var nested=0;var rejected=0;var retries=0;
        void Reject(Action action){try{action();}catch(InvalidOperationException){rejected++;return;}throw new Exception("Accepted invalid live cache context.");}
        foreach(var (trace,ti) in native.RootElement.GetProperty("cases").EnumerateArray().Select((v,i)=>(v,i)))
        {
            using var host=resources.CreateMainPoseHost(trace.GetProperty("profile").GetString()!,700,1700,7,enableMainInertia:false);
            var main=host.Main.CacheLifecycle;var provider=host.Main.Layer(LyraLayerHook.FullBody_Aiming).CacheLifecycle;
            AlsPoseCacheLifecycle Owner(int node)=>node==181?provider:main;
            int Actual(int node)=>node==181?78:node;
            int Index(int node)=>node==78?0:node==83?1:2;
            // Native starts with fresh original class instances. Seed only the
            // actual owners' cache histories; no production seeding API exists.
            var committed=typeof(AlsPoseCacheLifecycle).GetField("_committed",BindingFlags.NonPublic|BindingFlags.Instance)!;
            committed.SetValue(main,new[]{new AlsPoseCacheNodeHistory(78,default,default,default,0),new AlsPoseCacheNodeHistory(83,default,default,default,0)});
            committed.SetValue(provider,new[]{new AlsPoseCacheNodeHistory(78,default,default,default,0)});
            var init=new int[3];var bones=new int[3];var updates=new int[3];var eval=new int[3];var sourceWeight=new float[3];
            var traversal=new AlsPoseCacheTraversal(new(200,[78,83,181],[new(10,78),new(11,83),new(12,181)]),12);
            var sink=new UpdateSink((node,weight)=>Owner(node).RecordWeight(Actual(node),weight),(node,weight)=>{updates[Index(node)]++;sourceWeight[Index(node)]=weight;});
            var cache=new LyraMainPoseCacheScope(bank,Owner);var inner=new LyraMainPoseCacheScope(bank,Owner);
            var steps=requests.RootElement.GetProperty("cases")[ti].GetProperty("steps");var i=0;
            foreach(var row in trace.GetProperty("rows").EnumerateArray())
            {
                var step=steps[i];var op=step.GetProperty("op").GetString();var counter=new AlsGraphTraversalCounter(step.GetProperty("counter").GetInt16(),step.GetProperty("frame").GetUInt64());
                var frame=new object();var outputs=new List<float[]>();var beforeMain=main.History;var beforeProvider=provider.History;
                if(op is "init" or "bones")foreach(var node in new[]{78,83,181})
                {
                    if(op=="init")Owner(node).Initialize(Actual(node),counter,()=>init[Index(node)]++);
                    else Owner(node).CacheBones(Actual(node),counter,()=>bones[Index(node)]++);
                }
                else
                {
                    void ExecuteFrame()
                    {
                    main.Begin(frame);provider.Begin(frame);
                    Reject(()=>main.Initialize(78,counter,()=>{}));Reject(()=>provider.CacheBones(78,counter,()=>{}));
                    if(op=="update")
                    {
                        main.PostUpdate(frame,[]);provider.PostUpdate(frame,[]);traversal.Begin(new(i,0,1));
                        for(var n=0;n<3;n++)foreach(var w in step.GetProperty("weights")[n].EnumerateArray())
                            traversal.Use(10+n,new(new(i,0,1),w.GetSingle(),1f/60));
                        traversal.Drain(sink);
                    }
                    else
                    {
                        var token=new object();cache.Begin(token,frame,counter);
                        void Read(LyraMainPoseCacheScope scope,object t,int pass)
                        {
                            var values=new float[3];
                            foreach(var node in new[]{78,83,181})
                            {
                                var view=scope.Read(t,node,destination=>
                                {
                                    bank.Reference.CopyTo(destination.Pose);Array.Clear(destination.Curves);Array.Clear(destination.Attributes);destination.RootMotion=default;
                                    destination.Pose[0]=destination.Pose[0] with{Position=new(step.GetProperty("marker").GetSingle()+pass*10+Index(node)*.125f,0,0)};eval[Index(node)]++;
                                });
                                values[Index(node)]=(float)view.Pose[0].Position.X;
                            }
                            outputs.Add(values);
                        }
                        Read(cache,token,0);
                        if(op=="nested")
                        {
                            var other=new AlsGraphTraversalCounter(counter.Counter==32767?(short)-32768:(short)(counter.Counter+1),counter.GlobalFrame+1);
                            var innerToken=new object();inner.Begin(innerToken,frame,other);Read(inner,innerToken,1);inner.End();Read(cache,token,2);Read(cache,token,3);nested++;
                        }
                        else Read(cache,token,1);
                        var discarded=cache.Read(token,83,_=>throw new Exception("Repeated read unexpectedly evaluated."));
                        cache.End();Reject(()=>{_ = discarded.Pose.Length;});
                    }
                    }
                    ExecuteFrame();
                    Require(main.History.SequenceEqual(beforeMain)&&provider.History.SequenceEqual(beforeProvider),"Candidate cache history was published early.");
                    var candidateMain=main.Prepared;var candidateProvider=provider.Prepared;
                    main.Cancel();provider.Cancel();Require(main.History.SequenceEqual(beforeMain)&&provider.History.SequenceEqual(beforeProvider),"Cancelled cache history changed.");
                    var firstOutputs=outputs.Select(v=>v.ToArray()).ToArray();outputs.Clear();
                    ExecuteFrame();
                    Require(main.Prepared.SequenceEqual(candidateMain)&&provider.Prepared.SequenceEqual(candidateProvider),"Retried cache history differs.");
                    Require(outputs.Count==firstOutputs.Length&&outputs.Zip(firstOutputs).All(p=>p.First.SequenceEqual(p.Second)),"Retried scoped cache output differs.");
                    main.Commit(frame);provider.Commit(frame);retries++;
                    Reject(()=>main.EvaluationMatches(frame,83,counter));
                }
                var expectedOutputs=row.GetProperty("outputs");Require(outputs.Count==expectedOutputs.GetArrayLength(),"Native cache output count.");
                for(var pass=0;pass<outputs.Count;pass++)for(var n=0;n<3;n++)Require(outputs[pass][n]==expectedOutputs[pass][n].GetSingle(),$"Native scope value {ti}/{i}/{pass}/{n}");
                foreach(var (node,n) in new[]{78,83,181}.Select((node,n)=>(node,n)))
                {
                    var actual=Owner(node).History.Single(h=>h.Node==Actual(node));var expected=row.GetProperty("states")[n];
                    void Counter(AlsGraphTraversalCounter value,string name)
                    {var e=expected.GetProperty(name);Require(value.Counter==e.GetProperty("counter").GetInt16()&&(value.HasUpdated?checked((long)value.GlobalFrame):-1)==e.GetProperty("frame").GetInt64(),$"Native counter {ti}/{i}/{node}/{name}");fields+=2;}
                    Counter(actual.Initialization,"initialization");Counter(actual.Bones,"bones");Counter(actual.Evaluation,"evaluation");
                    Require(expected.GetProperty("update").GetProperty("counter").GetInt32()==-1&&expected.GetProperty("update").GetProperty("frame").GetInt32()==-1,"Original cache UpdateCounter changed.");
                    Require(actual.GlobalWeight==expected.GetProperty("weight").GetSingle()&&sourceWeight[n]==expected.GetProperty("sourceWeight").GetSingle(),"Native winning cache weight.");
                    Require(init[n]==expected.GetProperty("initializations").GetInt32()&&bones[n]==expected.GetProperty("boneCalls").GetInt32()&&updates[n]==2*expected.GetProperty("updates").GetInt32()&&eval[n]==2*expected.GetProperty("evaluations").GetInt32(),"Native source callback counts.");
                }
                rows++;i++;
            }
            evaluations+=eval.Sum();
        }
        Require(rows==90&&fields==1620&&evaluations>100&&nested==12&&retries>0&&rejected>100,
            $"Incomplete cache lifecycle coverage: rows={rows} fields={fields} evaluations={evaluations} nested={nested} retries={retries} rejected={rejected}");
        GD.Print($"LYRA_CACHE_OWNER_NATIVE_GODOT_OK profiles=3 nodes=9 rows={rows} counterFields={fields} sourceEvaluations={evaluations} nested={nested} retries={retries} rejected={rejected} realOwner=true naturalCounters=false fullGraphSchedule=false goalComplete=false");
    }
}

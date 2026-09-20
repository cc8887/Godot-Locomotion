using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPrecisePoseCacheTests
{
    private static readonly AlsPoseCacheDefinition Definition=new(3,[0],[new(1,0),new(2,0)]);
    private static readonly AlsGraphTraversalCounter Counter=new(0,10);
    [Fact]
    public void SharedAndNestedScopesPreserveUnroundedPayloadAndPresence()
    {
        var cache=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var committed=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var sink=new Sink(); var pose=new AlsPrecisePose[1]; var curves=new AlsInertialCurve[1];
        cache.BeginCandidate(new(1,0,1),committed);
        var outer=cache.PushScope();
        cache.EvaluatePrecise(1,Counter,outer,sink,pose,curves); var first=pose[0];
        Assert.NotEqual(first.Rotation,new AlsQuaternion(first.Rotation.ToSingle()));
        cache.EvaluatePrecise(2,Counter,outer,sink,pose,curves);
        Assert.Equal(first,pose[0]); Assert.Equal(1,sink.Calls); Assert.Equal(new AlsInertialCurve(0),curves[0]);
        var inner=cache.PushScope();
        cache.EvaluatePrecise(2,Counter,inner,sink,pose,curves);
        Assert.NotEqual(first,pose[0]); cache.PopScope(inner);
        cache.EvaluatePrecise(1,Counter,outer,sink,pose,curves);
        Assert.Equal(first,pose[0]); Assert.Equal(2,sink.Calls); cache.PopScope(outer);
    }
    [Fact]
    public void FailedPrecisePayloadCannotBeReusedAndRetryCopiesOnlyCommittedHistory()
    {
        var candidate=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var committed=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var sink=new Sink {Fail=true}; var pose=new AlsPrecisePose[1]; var curves=new AlsInertialCurve[1];
        candidate.BeginCandidate(new(1,0,1),committed); var scope=candidate.PushScope();
        Assert.Throws<InvalidOperationException>(()=>candidate.EvaluatePrecise(1,Counter,scope,sink,pose,curves));
        Assert.True(candidate.IsFaulted); Assert.Equal(0,committed.SourceEvaluations);
        candidate.PopScope(scope); sink.Fail=false;
        candidate.BeginCandidate(new(1,0,1),committed); scope=candidate.PushScope();
        candidate.EvaluatePrecise(1,Counter,scope,sink,pose,curves);
        Assert.False(candidate.IsFaulted); Assert.Equal(1,candidate.SourceEvaluations); candidate.PopScope(scope);
    }
    [Fact]
    public void PrecisionModesCannotShareBanksOrSilentlyProject()
    {
        var precise=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var single=new AlsPoseCacheEvaluation(Definition,1,1);
        Assert.Throws<InvalidOperationException>(()=>precise.BeginCandidate(new(1,0,1),single));
        precise.BeginCandidate(new(1,0,1),new(Definition,1,1,precise:true));
        var scope=precise.PushScope();
        Assert.Throws<InvalidOperationException>(()=>precise.Evaluate(1,Counter,scope,new Sink(),new AlsLocalPose[1],new AlsInertialCurve[1]));
        precise.PopScope(scope);
    }
    private sealed class Sink : IAlsPrecisePoseCachePoseSink
    {
        public int Calls; public bool Fail;
        public void InitializeSource(int node) { }
        public void CacheSourceBones(int node) { }
        public void EvaluateSource(int node,Span<AlsLocalPose> bones,Span<AlsInertialCurve> curves)=>throw new Exception("Unexpected single callback.");
        public void EvaluatePreciseSource(int node,Span<AlsPrecisePose> bones,Span<AlsInertialCurve> curves)
        {
            var q=new AlsQuaternion(.12345678901234,0,0,.992349998).Normalized();
            bones[0]=new(new(++Calls,0,0),q,AlsDoubleVector.One); curves[0]=new(0);
            if(Fail)throw new InvalidOperationException("late precise failure");
        }
    }

    [Fact]
    public void RepeatedPreciseCandidatesAllocateNothing()
    {
        var candidate=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var committed=new AlsPoseCacheEvaluation(Definition,1,1,precise:true);
        var sink=new Sink(); var pose=new AlsPrecisePose[1]; var curves=new AlsInertialCurve[1];
        for(var i=0;i<1000;i++)Run(i+1);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++)Run(i+1);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        void Run(int frame)
        {
            candidate.BeginCandidate(new(frame,0,1),committed); var scope=candidate.PushScope();
            candidate.EvaluatePrecise(1,Counter,scope,sink,pose,curves);
            candidate.EvaluatePrecise(2,Counter,scope,sink,pose,curves); candidate.PopScope(scope);
        }
    }
}

using System.Text.Json;
using System.Globalization;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredCrouchingHostProfile
{
    internal const string WalkPose = "/ALS/ALS/Animations/Grounded/Crouch/A_Als_Crouch_Walk_Pose.A_Als_Crouch_Walk_Pose";
    internal readonly AlsRefactoredStandingHostProfile Shared;
    internal readonly AlsRefactoredCrouchingResources Machine;
    internal readonly AlsRefactoredDirectionSourceProfile Direction;
    internal readonly AlsRefactoredDirectionPose DirectionPose;
    internal readonly AlsRefactoredStandingRestGraph RestGraph;
    internal readonly AlsRefactoredStanceCallbacks Callbacks;
    internal readonly AlsRefactoredStandingRestPose Rest;
    internal readonly AlsRefactoredRestMontagePose Montages;
    internal readonly AlsRefactoredBlendPoseSource Lean;
    internal readonly AlsPoseCacheDefinition Caches;
    internal readonly AlsPrecisePose[] Walk;
    internal readonly AlsInertialCurve[] WalkCurves;
    internal readonly float[] StopMask;
    internal readonly AlsSequenceMontageCommand StopCommand;
    private readonly string[] _curves;
    public ReadOnlySpan<string> CurveNames => _curves;
    public ReadOnlySpan<string> BoneNames => Rest.BoneNames;
    public ReadOnlySpan<int> Parents => Rest.Parents;
    public string CatalogDigest => Shared.CatalogDigest;

    public AlsRefactoredCrouchingHostProfile(AlsRefactoredStandingHostProfile shared, string machines, AlsRefactoredSkeletonCurves metadata)
    {
        Shared=shared; var catalog=shared.Catalog; Machine=new(machines,catalog); Callbacks=new(catalog,true);
        Direction=new(catalog,new(catalog,new(machines,catalog,true))); DirectionPose=new(catalog,Direction,shared.Triangles);
        RestGraph=new(catalog,true); Lean=new(shared.Triangles[AlsRefactoredMovementCacheProfile.LeanSource],catalog);
        var walk=catalog.CompileAbsolutePoseWithCurves(WalkPose);
        var initial=new AlsRefactoredStandingRestPose(catalog,RestGraph,metadata,[]);
        var curves=initial.CurveNames.ToArray().Concat(DirectionPose.CurveNames.ToArray()).Concat(Lean.CurveNames.ToArray())
            .Concat(walk.Curves.Names.ToArray()).Concat(["PoseCrouching","PoseMoving"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Montages=new(catalog,shared.Montages,curves);
        Rest=new(catalog,RestGraph,metadata,Montages.CurveNames.ToArray()); _curves=Rest.CurveNames.ToArray();
        if (!_curves.SequenceEqual(Montages.CurveNames.ToArray())) throw new ArgumentException("Crouching rest curve union differs.");
        Walk=new AlsPrecisePose[BoneNames.Length]; WalkCurves=new AlsInertialCurve[_curves.Length];
        var sampled=new AlsInertialCurve[walk.Curves.Names.Length];
        walk.Pose.CreateSampler(walk.Curves).Sample(0,true,false,false,Walk,sampled);
        for(var c=0;c<sampled.Length;c++) WalkCurves[Array.IndexOf(_curves,walk.Curves.Names[c])]=sampled[c];
        var payload=catalog.Read(AlsRefactoredRotatePlayers.Blueprint(true));
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("propertyIndex").GetInt32());
        void Link(int id,string property,int target)=>Expect(nodes[id].GetProperty("runtime").GetProperty(property),new{linkId=target,sourceLinkId=id});
        Link(2,"sourcePose",34); Link(34,"source",33);
        Expect(nodes[34].GetProperty("runtime"),new{defaultBlendProfile="",filteredCurves=new[]{"RotationYawSpeed"},filteredBones=Array.Empty<string>(),
            bResetOnBecomingRelevant=true,bForwardRequestsThroughSkippedCachedPoseNodes=true});
        Expect(nodes[2].GetProperty("runtime"),new{curveNames=new[]{"PoseCrouching"},curveValues=new[]{1},alpha=1,applyMode="Blend"});
        Expect(nodes[35].GetProperty("runtime"),new{curveNames=new[]{"PoseMoving"},curveValues=new[]{1},alpha=1,applyMode="Blend"});
        Expect(nodes[12].GetProperty("runtime"),new{curveNames=new[]{"FootLeftLock","FootRightLock"},curveValues=new[]{1,1},alpha=1,applyMode="Blend"});
        Link(1,"pose",44); Link(44,"source",109); Link(109,"source",35); Link(35,"sourcePose",42);
        Link(42,"base",43); Link(42,"additive",110); Link(43,"a",45); Link(43,"b",108);
        Link(24,"result",23); Link(23,"linkToCachingNode",1); Link(11,"linkToCachingNode",1);
        Link(16,"result",12); Link(12,"sourcePose",15); Link(15,"basePose",11);
        Expect(nodes[43].GetProperty("runtime"),new{alphaInputType="Float",bAlwaysUpdateChildren=false,bResetChildOnActivation=false});
        Expect(nodes[43].GetProperty("runtime").GetProperty("alphaScaleBiasClamp"),new{bMapRange=false,bClampResult=false,bInterpResult=true,scale=1,bias=0,interpSpeedIncreasing=10,interpSpeedDecreasing=10});
        Expect(nodes[42].GetProperty("runtime"),new{alpha=1,alphaInputType="Float",alphaScaleBias=new{scale=1,bias=0}});
        foreach(var id in new[]{45,14,13}) Expect(nodes[id].GetProperty("runtime"),new{sequence=WalkPose,explicitFrame=0,bUseExplicitFrame=true,bTeleportToExplicitTime=true});
        var layer=nodes[15].GetProperty("runtime");
        Expect(layer,new{bMeshSpaceRotationBlend=true,bMeshSpaceScaleBlend=false,curveBlendOption="Override",blendWeights=new[]{1,1}});
        StopMask=layer.GetProperty("perBoneBlendWeights").EnumerateArray().Select(w=>w.GetProperty("blendWeight").GetSingle()).ToArray();
        if(StopMask.Length!=BoneNames.Length||StopMask.Any(w=>!float.IsFinite(w)||w<0||w>1)) throw new ArgumentException("Crouching Stop mask differs.");
        // Both leg evaluators use the same explicit frame. Keep their two graph
        // identities above, while sharing immutable sampled data.
        var order=payload.GetProperty("compiled").GetProperty("orderedSavedPoseNodes")[0].GetProperty("compiledNodeIndices").EnumerateArray()
            .Select(i=>nodes.Values.Single(n=>n.GetProperty("compiledNodeIndex").GetInt32()==i.GetInt32()).GetProperty("propertyIndex").GetInt32()).ToArray();
        Caches=new(nodes.Count,order,Direction.Caches.Reads.ToArray().Concat([new AlsPoseCacheReadBinding(23,1),new AlsPoseCacheReadBinding(11,1)]).ToArray());
        var blueprint=AlsRefactoredRotatePlayers.Blueprint(true);
        var function=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!,blueprint,blueprint+":PlayStopTransitionAnimation"),true);
        var call=function.Nodes.Single(n=>n.Kind=="K2Node_CallFunction"&&n.Member=="PlayTransitionRightAnimation");
        function.Function(call,"PlayTransitionRightAnimation","ALS.AlsAnimationInstance");
        float Number(string key)=>float.Parse(function.Literal(call,key),CultureInfo.InvariantCulture);
        var right=shared.QuickStop.Assets[3];
        StopCommand=new(right.AnimationId,right.Slot,Number("PlayRate"),Number("StartTime"),Number("BlendInDuration"),Number("BlendOutDuration"));
        if(function.Literal(call,"bFromStandingIdleOnly")!="false") throw new ArgumentException("Crouching Stop is not standing-idle gated.");
    }
    public AlsRefactoredCrouchingHost CreateRuntime(uint character,uint generation)=>new(this,character,generation);
}

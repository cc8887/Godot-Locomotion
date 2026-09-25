using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Node68 is outside inertia118: PoseStanding is applied to its final
/// output, never fed back into the inertial history.</summary>
public sealed class AlsRefactoredStandingOutput
{
    private readonly AlsRefactoredStandingInertialization _source;
    private readonly AlsRefactoredStandingPose _profile;
    private readonly string[] _curves;
    private readonly int _standing;
    public ReadOnlySpan<string> CurveNames=>_curves;
    public AlsRefactoredStandingOutput(AlsRefactoredAnimationCatalog catalog,AlsRefactoredStandingPose profile,AlsRefactoredStandingInertialization source)
    {
        if(catalog.IndexDigest!=profile.Resources.CatalogDigest || !ReferenceEquals(source.Profile,profile))throw new ArgumentException("Foreign Standing output source.");
        ValidateNode(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)));
        _profile=profile;_source=source;
        _curves=profile.CurveNames.ToArray().Concat(new[]{"PoseStanding"}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _standing=Array.FindIndex(_curves,n=>n.Equals("PoseStanding",StringComparison.OrdinalIgnoreCase));
    }
    internal static void ValidateNode(JsonElement payload)
    {
        var blueprint=AlsRefactoredRotatePlayers.Blueprint(false);
        var nodes=payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("propertyIndex").GetInt32());
        var node=nodes[68];Expect(node,new{@class="AnimGraphNode_ModifyCurve"});
        var runtime=node.GetProperty("runtime");
        foreach(var p in new[]{runtime,node.GetProperty("authoredProperties").GetProperty("Node")})
        {
            Expect(p,new{curveNames=new[]{"PoseStanding"},alpha=1,applyMode="Blend"});
            if(p.GetProperty("curveMap").EnumerateObject().Any())throw new ArgumentException("Unexpected Standing output curve map.");
            foreach(var callback in new[]{"initialUpdateFunction","becomeRelevantFunction","updateFunction"})Expect(p.GetProperty(callback),new{functionName="None"});
        }
        Expect(runtime,new{curveValues=new[]{1}});
        Expect(node.GetProperty("authoredProperties").GetProperty("Node"),new{curveValues=new[]{0}});
        Expect(runtime.GetProperty("sourcePose"),new{linkId=118,sourceLinkId=68});
        var graph=new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!,blueprint,blueprint+":AnimGraph"),true);
        var authored=graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
        var (child,pin)=graph.FollowReroutes(authored,"SourcePose");
        if(authored.Kind!="AnimGraphNode_ModifyCurve" || pin.Name!="Pose" || float.Parse(graph.Literal(authored,"CurveValues_0"),System.Globalization.CultureInfo.InvariantCulture)!=1 ||
            authored.Body.Contains("bIsBound=True",StringComparison.Ordinal) || nodes[118].GetProperty("path").GetString()!=blueprint+":AnimGraph."+child.Name)
            throw new ArgumentException("Standing output authored connection or value differs.");
    }
    public void Evaluate(in AlsFrameIdentity identity,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        _source.ValidateIdentity(identity);
        if(pose.Length!=_profile.BoneNames.Length||curves.Length!=_curves.Length)throw new ArgumentException("Standing output layout differs.");
        var sourcePose=_source.Pose;var sourceCurves=_source.Curves;
        sourcePose.CopyTo(pose);curves.Clear();sourceCurves.CopyTo(curves);
        curves[_standing]=AlsStandingCycleCurves.ModifyBlend(curves[_standing],1,1);
    }
}

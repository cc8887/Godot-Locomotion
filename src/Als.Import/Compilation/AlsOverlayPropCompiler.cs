using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed record AlsOverlayPropBinding(AlsOverlayKind Overlay, bool Skeletal, int MeshId,
    string Socket, int LogicalBone, Vector3 Offset, int AnimationId)
{ public bool HasProp => MeshId >= 0; }

public sealed class AlsOverlayPropProfile(string digest, AlsOverlayPropBinding[] bindings, int bowAnimation, string drawCurve)
{
    private readonly AlsOverlayPropBinding[] _bindings = bindings.ToArray();
    public string Digest { get; } = digest;
    public int BowAnimationId { get; } = bowAnimation;
    public string DrawCurve { get; } = drawCurve;
    public AlsOverlayPropBinding Get(AlsOverlayKind overlay) => (uint)overlay < _bindings.Length
        ? _bindings[(int)overlay] : throw new ArgumentOutOfRangeException(nameof(overlay));
}

// Compile the native switch, attachment policy and Draw evaluator. Trace rows
// corroborate the graph; they are never used as a hand-authored equipment table.
public static class AlsOverlayPropCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_AnimMan_CharacterBP.ALS_AnimMan_CharacterBP";
    private const string Bow = "/Game/AdvancedLocomotionV4/Props/Meshes/Bow_AnimBP.Bow_AnimBP";
    public static AlsOverlayPropProfile Compile(string json, AlsAnimationSetDefinition set, int skeletonId)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root,"source") == Source, "Foreign prop Blueprint.");
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g,"path"), g => Text(g,"nativeText"));
        Graph Read(string name) => new(graphs[Source + ":" + name], true);
        var attach = Read("AttachToHand"); var call = attach.One("K2Node_CallFunction","K2_AttachToComponent");
        foreach (var rule in new[] { "LocationRule", "RotationRule", "ScaleRule" })
            Require(attach.Literal(call,rule) == "SnapToTarget", "Unsupported prop attachment rule.");
        Require(attach.Follow(call,"self").Item1.Member == "HeldObjectRoot" &&
            attach.Follow(call,"Parent").Item1.Member == "Mesh", "Wrong prop attachment owner.");
        var select = attach.Follow(call,"SocketName").Item1;
        Require(select.Kind == "K2Node_Select" && attach.Follow(select,"Index").Item1.Member == "LeftHand", "Wrong attachment selector.");
        var right = attach.Literal(select,"Option 0"); var left = attach.Literal(select,"Option 1");
        Require(right == "VB RHS_ik_hand_gun" && left == "VB LHS_ik_hand_gun", "Missing native virtual-bone attachment.");
        var location = Next(attach,call,"then");
        Require(location.Member == "K2_SetRelativeLocation" && attach.Follow(location,"NewLocation").Item1.Member == "Offset" &&
            attach.Follow(location,"self").Item1.Member == "HeldObjectRoot", "Wrong prop offset consumer.");
        var sequence = Next(attach,attach.One("K2Node_FunctionEntry","AttachToHand"),"then");
        Require(sequence.Kind == "K2Node_ExecutionSequence" && Next(attach,sequence,"then_0").Member == "ClearHeldObject" &&
            Next(attach,sequence,"then_3") == call, "Prop assignment must clear the old object before attaching.");
        CheckAssignment(attach, sequence, "then_1", "NewStaticMesh", "SetStaticMesh", "StaticMesh");
        var skeletalAssign = CheckAssignment(attach, sequence, "then_2", "NewSkeletalMesh", "SetSkinnedAssetAndUpdate", "SkeletalMesh");
        var animGuard = Next(attach,skeletalAssign,"then");
        Require(animGuard.Member == "NewAnimClass", "Wrong prop animation guard.");
        var animAssign = Next(attach,animGuard,"then");
        Require(animAssign.Member == "SetAnimInstanceClass" && attach.Follow(animAssign,"NewClass").Item1 == animGuard &&
            attach.Follow(animAssign,"self").Item1.Member == "SkeletalMesh", "Wrong prop animation assignment.");

        var clear = Read("ClearHeldObject"); var clearCall = Next(clear,clear.One("K2Node_FunctionEntry","ClearHeldObject"),"then");
        foreach (var (member, target, value) in new[] { ("SetStaticMesh","StaticMesh","NewMesh"),
            ("SetSkinnedAssetAndUpdate","SkeletalMesh","NewMesh"), ("SetAnimInstanceClass","SkeletalMesh","NewClass") })
        {
            Require(clearCall.Member == member && clear.Follow(clearCall,"self").Item1.Member == target &&
                ObjectLiteral(clear,clearCall,value) == "", "Native clear must reset both meshes and animation class.");
            if (member != "SetAnimInstanceClass") clearCall = Next(clear,clearCall,"then");
        }
        var changed = Read("OnOverlayStateChanged"); var parent = Next(changed,changed.One("K2Node_FunctionEntry","OnOverlayStateChanged"),"then");
        Require(parent.Kind == "K2Node_CallParentFunction" && parent.Member == "OnOverlayStateChanged" &&
            Next(changed,parent,"then").Member == "UpdateHeldObject", "Missing Overlay change gameplay consumer.");

        var bowGraph = new Graph(graphs[Bow + ":AnimGraph"],true);
        var evaluator = bowGraph.Nodes.Single(n => n.Kind == "AnimGraphNode_SequenceEvaluator");
        var animationPath = Regex.Match(evaluator.Body,"Sequence=\"/Script/Engine.AnimSequence'([^']+)'\"").Groups[1].Value;
        var animation = set.Animations.Single(a => a.ObjectPath == animationPath);
        Require(bowGraph.Follow(evaluator,"ExplicitTime").Item1.Member == "Draw" &&
            bowGraph.Follow(bowGraph.Nodes.Single(n=>n.Kind=="AnimGraphNode_Root"),"Result").Item1 == evaluator &&
            !evaluator.Body.Contains("bTeleportToExplicitTime=False",StringComparison.Ordinal), "Bow must teleport to its Draw evaluator time.");
        var update = Read("UpdateHeldObjectAnimations"); var draw = update.One("K2Node_VariableSet","Draw");
        var drawSwitch = Next(update,update.One("K2Node_FunctionEntry","UpdateHeldObjectAnimations"),"then");
        Require(drawSwitch.Kind == "K2Node_SwitchEnum" && update.Follow(drawSwitch,"Selection").Item1.Member == "OverlayState",
            "Bow update lost its Overlay gate.");
        var drawBranch = drawSwitch.Pins.Values.Single(p=>p.Output && p.Links.Length != 0);
        Require(AlsOverlayStateCompiler.ParseOverlayLiteral(drawBranch.Name) == AlsOverlayKind.Bow,"Draw must only update the Bow Overlay.");
        var cast = Next(update,drawSwitch,drawBranch.Name);
        Require(cast.Kind == "K2Node_DynamicCast" && cast.Body.Contains("TargetType=\"/Script/Engine.AnimBlueprintGeneratedClass'" + Bow + "_C'\"",StringComparison.Ordinal) &&
            Next(update,cast,"then") == draw && update.Follow(draw,"self").Item1 == cast,"Draw targets the wrong AnimInstance.");
        var instance = update.Follow(cast,"Object").Item1;
        Require(instance.Member == "GetAnimInstance" && update.Follow(instance,"self").Item1.Member == "SkeletalMesh", "Wrong held-object animation component.");
        var curve = update.Follow(draw,"Draw").Item1;
        Require(curve.Member == "GetAnimCurveValue", "Bow Draw lost its character curve source.");
        update.Self(curve);
        var curveName = update.Literal(curve,"CurveName");
        Require(curveName == "Enable_SpineRotation", "Unsupported bow Draw curve.");

        var graph = Read("UpdateHeldObject"); var branch = Next(graph,graph.One("K2Node_FunctionEntry","UpdateHeldObject"),"then");
        Require(branch.Kind == "K2Node_SwitchEnum" && graph.Follow(branch,"Selection").Item1.Member == "OverlayState", "Wrong held-object selector.");
        var bindings = new AlsOverlayPropBinding[13];
        foreach (var pin in branch.Pins.Values.Where(p => p.Output))
        {
            var overlay = AlsOverlayStateCompiler.ParseOverlayLiteral(pin.Name); var node = Next(graph,branch,pin.Name);
            while (node.Kind == "K2Node_Knot") node = Next(graph,node,"OutputPin");
            Require(bindings[(int)overlay] is null, "Duplicate Overlay equipment branch.");
            if (node.Member == "ClearHeldObject")
            { bindings[(int)overlay] = new(overlay,false,-1,"",-1,Vector3.Zero,-1); continue; }
            Require(node.Member == "AttachToHand", "Unsupported held-object branch."); graph.Self(node);
            var staticMesh = ObjectLiteral(graph,node,"NewStaticMesh"); var skeletalMesh = ObjectLiteral(graph,node,"NewSkeletalMesh");
            Require((staticMesh.Length == 0) != (skeletalMesh.Length == 0), "An Overlay must select exactly one mesh.");
            var skeletal = skeletalMesh.Length != 0;
            var meshId = skeletal ? set.SkeletalMeshes.Single(m=>m.ObjectPath==skeletalMesh && m.Prop).Id
                : set.StaticMeshes.Single(m=>m.ObjectPath==staticMesh && m.Prop).Id;
            var side = graph.Literal(node,"LeftHand"); Require(side is "true" or "false", "Invalid prop hand selector.");
            var socket = side == "true" ? left : right; var bone = set.Skeletons[skeletonId].GetLogicalBoneId(socket);
            Require(bone >= 0 && set.Skeletons[skeletonId].LogicalBones[bone].PhysicalId == -1 &&
                set.Skeletons[skeletonId].LogicalBones[set.Skeletons[skeletonId].LogicalBones[bone].ParentLogicalId].PhysicalId >= 0,
                "Prop virtual bone requires a physical parent in the imported rig.");
            var offset = graph.Literal(node,"Offset").Split(',').Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();
            Require(offset.Length == 3 && offset.All(float.IsFinite), "Invalid attachment offset.");
            var animClass = ObjectLiteral(graph,node,"NewAnimClass");
            Require(animClass == "" || overlay == AlsOverlayKind.Bow && animClass == Bow + "_C", "Unsupported prop AnimBlueprint.");
            bindings[(int)overlay] = new(overlay,skeletal,meshId,socket,bone,new Vector3(offset[0],-offset[1],offset[2])*.01f,
                animClass.Length == 0 ? -1 : animation.Id);
        }
        Require(bindings.All(b=>b is not null) && bindings.Count(b=>b.HasProp)==8 && bindings[(int)AlsOverlayKind.Bow].AnimationId==animation.Id,
            "Incomplete native Overlay equipment table.");
        // Component-local offsets are inherited into every mesh. Reject unsupported transforms.
        var components = root.GetProperty("components").EnumerateArray().ToArray();
        Require(components.Select(c => Text(c,"name")).Order().SequenceEqual(new[] { "HeldObjectRoot", "SkeletalMesh", "StaticMesh" }),
            "Missing or foreign prop component defaults.");
        foreach (var component in components)
            Require(!Regex.IsMatch(Text(component,"nativeText"),@"(?m)^   (RelativeLocation|RelativeRotation|RelativeScale3D|bAbsoluteLocation|bAbsoluteRotation|bAbsoluteScale)="),
                "Prop component has an uncompiled relative transform.");
        var cases = root.GetProperty("cases").EnumerateArray().ToArray(); Require(cases.Length >= 13,"Missing native equipment oracle.");
        Require(cases.Select(c=>c.GetProperty("overlayValue").GetInt32()).Distinct().Order().SequenceEqual(Enumerable.Range(0,13)),
            "Native equipment oracle does not cover all Overlays.");
        foreach (var row in cases)
        {
            var binding = bindings[row.GetProperty("overlayValue").GetInt32()];
            var expected = !binding.HasProp ? null : binding.Skeletal ? set.SkeletalMeshes[binding.MeshId].ObjectPath : set.StaticMeshes[binding.MeshId].ObjectPath;
            Require(row.GetProperty(binding.Skeletal ? "skeletalMesh" : "staticMesh").GetString() == expected &&
                row.GetProperty(binding.Skeletal ? "staticMesh" : "skeletalMesh").ValueKind == JsonValueKind.Null,
                "Native execution disagrees with the compiled prop branch.");
            if (binding.HasProp) Require(Text(row,"socket")==binding.Socket,"Native socket differs from graph selection.");
            Require(row.GetProperty("animClass").GetString() == (binding.AnimationId < 0 ? null : Bow + "_C"),
                "Native animation class differs from the compiled equipment.");
            if (binding.HasProp)
            {
                var position = row.GetProperty("locationCm").EnumerateArray().Select(v=>v.GetSingle()).ToArray();
                Require(position.Length == 3 && Vector3.Distance(new(position[0]*.01f,-position[1]*.01f,position[2]*.01f),binding.Offset) < .000001f &&
                    row.GetProperty("rotationDegrees").EnumerateArray().All(v=>v.GetDouble()==0) &&
                    row.GetProperty("scale").EnumerateArray().All(v=>v.GetDouble()==1), "Native component transform differs from attachment rules.");
            }
        }
        return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),bindings,animation.Id,curveName);
    }
    private static Node CheckAssignment(Graph graph, Node sequence, string pin, string parameter, string setter, string target)
    {
        var guard=Next(graph,sequence,pin); Require(guard.Member==parameter,"Wrong guarded prop parameter.");
        var call=Next(graph,guard,"then");
        Require(call.Member==setter && graph.Follow(call,"NewMesh").Item1==guard && graph.Follow(call,"self").Item1.Member==target,
            "Wrong prop mesh assignment."); return call;
    }
    private static Node Next(Graph graph, Node node, string name)
    {
        var pin=node.Pins.Values.Single(p=>p.Name==name && p.Output);
        var links=Regex.Matches(pin.Links,@"(\w+) (\w+),"); Require(links.Count==1,"Ambiguous prop execution path.");
        var next=graph.Named(links[0].Groups[1].Value); var target=next.Pins[links[0].Groups[2].Value];
        Require(!target.Output && target.Links.Contains(node.Name+" "+pin.Id+",",StringComparison.Ordinal),"Nonreciprocal prop execution link."); return next;
    }
    private static string ObjectLiteral(Graph graph, Node node, string name)
    {
        _=graph.Literal(node,name); var pin=node.Pins.Values.Single(p=>p.Name==name);
        var line=Regex.Matches(node.Body,@"CustomProperties Pin [^\r\n]+").Single(m=>m.Value.Contains("PinId="+pin.Id+",",StringComparison.Ordinal)).Value;
        return Regex.Match(line,"(?:^|,)DefaultObject=\"([^\"]+)\"").Groups[1].Value;
    }
    private static string Text(JsonElement row,string name)=>row.GetProperty(name).GetString()!;
    private static void Require(bool value,string message) { if(!value)throw new ArgumentException(message); }
}

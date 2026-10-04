using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLinkedLayerBindingSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Linked layer binding smoke failed: " + error); GetTree().Quit(1); }
    }

    private void Run()
    {
        var contractsBytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/linked_layer_contracts.json");
        var inventoryBytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/linked_layer_inventory.json");
        var contracts = LyraLinkedLayerContracts.Parse(contractsBytes, inventoryBytes);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/linked_layer_binding_native.json"));
        var oracle = native.RootElement;
        if (oracle.GetProperty("schemaVersion").GetInt32() != 1 ||
            oracle.GetProperty("nativeOperator").GetString() != "UAnimInstance.LinkAnimClassLayers" ||
            oracle.GetProperty("contractsSha256").GetString() != Convert.ToHexString(SHA256.HashData(contractsBytes)).ToLowerInvariant())
            throw new InvalidOperationException("Stale native layer-binding oracle.");
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true, includePistol: true, includeRifle: true);
        AddChild(rig.Root);
        var unarmed = new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining);
        var router = new LyraLinkedLayerRouter(rig.Catalog, unarmed, rig.Auxiliary, rig.Remaining, rig.Pistol, rig.Rifle);
        var motion = new LyraUnarmedMotion(rig, router,
            LyraUnarmedTiming.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, rig.Pistol, rig.Rifle),
            LyraUnarmedNotifies.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, rig.Pistol, rig.Rifle),
            LyraUnarmedRootMotion.Load(rig.Catalog, rig.Auxiliary, rig.Remaining, rig.Pistol, rig.Rifle), unarmed.PlaybackDefaults);
        var nativeOwners = new Dictionary<int, LyraItemLayerInstance>();
        var bindings = contracts.CreateBindings();
        bindings.Commit(bindings.PrepareLink(motion.ItemLayerInstance!.Contract.ClassPath));
        var bindingOwners = new Dictionary<int, long>();
        var profiles = oracle.GetProperty("profiles");
        var steps = oracle.GetProperty("result").GetProperty("steps");
        var repeats = 0;
        for (var step = 0; step < steps.GetArrayLength(); step++)
        {
            var before = motion.ItemLayerInstance!;
            var revision = motion.LayerRevision;
            // Drive the actual stateful additive graph before every binding.
            // Rebinding the same class must preserve AirIdentity and the visible pose.
            motion.Advance(new(Vector2.Zero, Vector2.Zero, LyraGait.Jog, 1d / 60, 0, 0, 0,
                IsOnGround: false, VerticalVelocity: -20, GroundDistanceCm: 300, AimPitchDegrees: 25));
            var pose = new AlsLocalPose[68];
            LyraUnarmedAimOffset.CapturePose(rig.Skeleton, pose);
            var clock = rig.Player.CurrentAnimationPosition;
            var phase = motion.Phase;
            var frames = motion.PoseLayerFrames;
            var row = steps[step];
            var profile = profiles[step].GetString();
            ILyraItemAnimationLayers provider = profile switch
            {
                "unarmed" => new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
                "pistol" => new LyraPistolAnimationLayers(rig.Pistol!),
                "rifle" => new LyraRifleAnimationLayers(rig.Rifle!),
                _ => throw new InvalidOperationException("Unsupported oracle profile."),
            };
            motion.LinkLayer(provider);
            var previousBindings = bindings.Targets;
            var cancelledBindings = bindings.PrepareLink(provider.AnimationClassPath);
            bindings.Cancel(cancelledBindings);
            if (!previousBindings.SequenceEqual(bindings.Targets))
                throw new InvalidOperationException("Cancelled binding changed committed layer identity.");
            var nextBindings = bindings.PrepareLink(provider.AnimationClassPath);
            if (!cancelledBindings.Targets.SequenceEqual(nextBindings.Targets))
                throw new InvalidOperationException("Binding retry changed ownership.");
            bindings.Commit(nextBindings);
            var instance = motion.ItemLayerInstance!;
            var sameClass = before.Contract.ClassPath == provider.AnimationClassPath;
            if (ReferenceEquals(instance, before) != sameClass ||
                motion.LayerRevision != revision + (sameClass ? 0 : 1) || motion.Phase != phase ||
                rig.Player.CurrentAnimationPosition != clock || motion.PoseLayerFrames != frames)
                throw new InvalidOperationException("Linked layer ownership changed character state or time.");
            if (sameClass)
            {
                var after = new AlsLocalPose[68];
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, after);
                if (instance.Additives.State != LyraAdditiveState.AirIdentity || !pose.SequenceEqual(after))
                    throw new InvalidOperationException("Same-class relink reset a live subgraph or its visible pose.");
                if (step > 0) repeats++;
            }
            else if (instance.Additives.State != LyraAdditiveState.Identity)
                throw new InvalidOperationException("New class inherited a previous layer's local state.");
            if (row.GetProperty("linkedInstances").GetInt32() != 1 || row.GetProperty("nodes").GetArrayLength() != 14)
                throw new InvalidOperationException("Native group shape changed.");
            foreach (var node in row.GetProperty("nodes").EnumerateArray())
            {
                var owner = node.GetProperty("owner").GetInt32();
                var target = bindings.Target(node.GetProperty("node").GetString()!);
                if (target.Class != node.GetProperty("class").GetString() ||
                    target.ReceiveNotifies != node.GetProperty("receiveNotifies").GetBoolean() ||
                    target.PropagateNotifies != node.GetProperty("propagateNotifies").GetBoolean() ||
                    bindingOwners.TryGetValue(owner, out var bindingOwner) && bindingOwner != target.Instance ||
                    bindingOwners.Any(p => p.Key != owner && p.Value == target.Instance))
                    throw new InvalidOperationException("Generic binding identity differs from original UE operation.");
                bindingOwners[owner] = target.Instance;
                if (nativeOwners.TryGetValue(owner, out var previous) && !ReferenceEquals(previous, instance))
                    throw new InvalidOperationException("Godot instance lifetime differs from native binding.");
                nativeOwners[owner] = instance;
                var hook = Enum.Parse<LyraLayerHook>(node.GetProperty("layer").GetString()!);
                if (instance.Contract.ClassPath != node.GetProperty("class").GetString() ||
                    instance.Contract.Functions[hook].Group != "ItemAnimLayers" ||
                    instance.ReceiveNotifies != node.GetProperty("receiveNotifies").GetBoolean() ||
                    instance.PropagateNotifies != node.GetProperty("propagateNotifies").GetBoolean() ||
                    !contracts.CallSites.Any(v => v.Node == node.GetProperty("node").GetString() && v.Hook == hook))
                    throw new InvalidOperationException("Compiled/native linked binding differs.");
            }
        }
        if (steps.GetArrayLength() != 8 || nativeOwners.Count != 4 || bindingOwners.Count != 4 || repeats != 4 || motion.LayerRevision != 3)
            throw new InvalidOperationException("Incomplete native binding coverage.");
        var rejected = 0;
        foreach (var mutation in new Action<JsonNode>[]
        {
            n => n["inventorySha256"] = new string('0', 64),
            n => n["classes"]!["interface"]!["functions"]![0]!["group"] = "OtherGroup",
            n => n["classes"]!["interface"]!["functions"]!.AsArray().RemoveAt(0),
            n => n["classes"]!["interface"]!["functions"]![0]!["inputPoses"]![0] = "WrongInput",
            n => n["classes"]!["interface"]!["functions"]!.AsArray().Single(v =>
                v!["name"]!.GetValue<string>() == "FullBody_Aiming")!["inputProperties"]![0]!["functionType"] = "float",
            n => n["classes"]!["main"]!["linkedNodes"]!.AsArray()[0] =
                n["classes"]!["main"]!["linkedNodes"]!.AsArray()[1]!.DeepClone(),
        })
        {
            var changed = JsonNode.Parse(contractsBytes)!;
            mutation(changed);
            try { LyraLinkedLayerContracts.Parse(Encoding.UTF8.GetBytes(changed.ToJsonString()), inventoryBytes); }
            catch (InvalidOperationException) { rejected++; continue; }
            throw new InvalidOperationException("Unsafe layer contract was accepted.");
        }
        GD.Print($"LYRA_LINKED_BINDING_OK steps=8 hooks=14 owners={nativeOwners.Count} sameClassReuse={repeats} " +
            $"rejected={rejected} group=ItemAnimLayers parameters=double,double skeleton=68");
    }
}

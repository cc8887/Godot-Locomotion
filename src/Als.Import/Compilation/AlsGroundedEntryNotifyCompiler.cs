using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;
using static GodotAls.Import.Compilation.AlsOverlayTransitionCompiler;

namespace GodotAls.Import.Compilation;

public sealed record AlsGroundedEntryNotifyProfile(AlsGroundedEntryNotifyBinding Binding, int ResetNotifyIndex);

public static class AlsGroundedEntryNotifyCompiler
{
    private const string Blueprint = "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify";
    private const string EnumPath = "/Game/AdvancedLocomotionV4/Data/Enums/GroundedEntryState.GroundedEntryState";
    private const string AnimBlueprint = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";

    public static AlsGroundedEntryNotifyProfile Compile(string json, string eventGraphJson,
        AlsAnimationSetDefinition set, AlsGroundedMachinesProfile machines, AlsP5aAnimationRuntimeProfile runtime)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "enumPath") == EnumPath,
            "Foreign grounded entry semantics.");
        var asset = set.Animations.Single(a => a.ObjectPath == Text(root, "source"));
        var row = root.GetProperty("notifies").EnumerateArray().Single();
        Require(Text(row, "classPath") == Blueprint + "_C", "Foreign grounded entry class.");
        var entry = asset.Timeline.Single(t => t.SourceIndex == row.GetProperty("sourceIndex").GetInt32());
        Require(entry.SourceClassPath == Text(row, "classPath") && entry.DurationSeconds == 0 &&
            entry.Kind is AlsCompiledTimelineEventKind.Generic or AlsCompiledTimelineEventKind.SetGroundedEntry,
            "Grounded entry timeline is not the native instant event.");
        var native = Text(row, "nativeText");
        Require(native.StartsWith("Begin Object Class=" + Blueprint + "_C ", StringComparison.Ordinal) &&
            native.Contains("'" + Text(row, "objectPath") + "'", StringComparison.Ordinal) &&
            Text(row, "objectPath").StartsWith(asset.ObjectPath + ":", StringComparison.Ordinal), "Foreign notify object.");
        var token = Regex.Matches(native, @"(?m)^   Grounded Entry State=(\w+)\r?$").Single().Groups[1].Value;
        var enumText = Text(root, "enumText");
        var labels = Regex.Matches(enumText, @"\(""(\w+)"", NSLOCTEXT\(""[^""]*"", ""[^""]*"", ""([^""]*)""\)\)")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Require(enumText.Contains("'" + EnumPath + "'", StringComparison.Ordinal) && labels.Count == 2 &&
            labels.GetValueOrDefault("NewEnumerator0") == "None" && labels.GetValueOrDefault("NewEnumerator2") == "Roll",
            "Grounded enum mapping differs.");
        var mode = token switch { "NewEnumerator0" => AlsTimelineGroundedEntryMode.None,
            "NewEnumerator2" => AlsTimelineGroundedEntryMode.FromRoll, _ => throw new ArgumentException("Unknown grounded entry value.") };
        if (entry.Kind == AlsCompiledTimelineEventKind.SetGroundedEntry)
            Require((int)entry.Payload.GroundedEntryMode == (int)mode, "Manifest and native grounded entry disagree.");
        var function = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == Blueprint + ":Received_Notify");
        var graph = new Graph(Text(function, "nativeText"), true);
        Require(graph.Nodes.Count() == 5, "Grounded notify has unsupported extra operations.");
        var begin = graph.One("K2Node_FunctionEntry", "Received_Notify");
        var call = graph.One("K2Node_Message", "BPI_SetGroundedEntryState");
        Require(call.Body.Contains("ALS_Animation_BPI.ALS_Animation_BPI_C'", StringComparison.Ordinal), "Foreign animation interface.");
        graph.Link(call, "execute", begin, "then");
        var getter = graph.One("K2Node_VariableGet", "Grounded Entry State"); graph.Self(getter);
        Require(getter.Body.Contains("'" + EnumPath + "'", StringComparison.Ordinal), "Foreign grounded property enum.");
        graph.Link(call, "GroundedEntryState", getter, "Grounded Entry State");
        var instance = graph.One("K2Node_CallFunction", "GetAnimInstance");
        graph.Function(instance, "GetAnimInstance", "Engine.SkeletalMeshComponent");
        graph.Link(instance, "self", begin, "MeshComp"); graph.Link(call, "self", instance, "ReturnValue");
        graph.Link(graph.One("K2Node_FunctionResult", "Received_Notify"), "execute", call, "then");

        using var events = JsonDocument.Parse(eventGraphJson); var eventRoot = events.RootElement;
        Require(Text(eventRoot, "source") == AnimBlueprint, "Foreign animation notify consumers.");
        var eventRow = eventRoot.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "path") == AnimBlueprint + ":EventGraph");
        var consumers = new Graph(Text(eventRow, "nativeText"), true);
        var reset = Event(consumers, "K2Node_Event", "AnimNotify_Reset-GroundedEntryState");
        var setter = Next(consumers, reset, "then"); consumers.Self(setter);
        Require(setter.Kind == "K2Node_VariableSet" && setter.Member == "GroundedEntryState" &&
            consumers.Literal(setter, "GroundedEntryState") == "NewEnumerator0" &&
            setter.Body.Contains("'" + EnumPath + "'", StringComparison.Ordinal), "Grounded reset no longer assigns None.");
        Unlinked(setter, "then");
        var resetId = machines.NotifyNames.Single(p => p.Value == "Reset-GroundedEntryState").Key;
        Require(machines.Main.Runtime.States[0].EndNotify == resetId &&
            machines.Main.Runtime.States[1..].ToArray().All(s => s.EndNotify != resetId),
            "Grounded Entry must reset the selection on exit.");
        var semantic = runtime.EventSemantics.Single(s => s.Kind == AlsCompiledTimelineEventKind.SetGroundedEntry);
        Require(semantic.SemanticId == (int)semantic.Kind, "Grounded entry semantic ID differs from the runtime binding contract.");
        return new(new(entry.EventId, asset.Id, mode, semantic.SemanticId), resetId);
    }

    private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}

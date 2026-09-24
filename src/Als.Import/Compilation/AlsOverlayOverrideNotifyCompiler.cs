using System.Text.Json;
using GodotAls.Core.Events;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsOverlayOverrideNotifyProfile
{
    private readonly Dictionary<(int Action, int Montage, int Event), int> _values;
    internal AlsOverlayOverrideNotifyProfile(Dictionary<(int, int, int), int> values) => _values = new(values);
    public int Advance(int state, in AlsEventBuffer events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            if (!_values.TryGetValue((item.SourceActionId, item.SourceAnimationId, item.EventId), out var value)) continue;
            if (item.Phase == AlsAnimationEventPhase.Begin) state = value;
            else if (item.Phase == AlsAnimationEventPhase.End) state = 0;
        }
        return state;
    }
}

public static class AlsOverlayOverrideNotifyCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/OverlayOverride_NotifyState.OverlayOverride_NotifyState";
    public static AlsOverlayOverrideNotifyProfile Compile(string selectionJson, string notifyJson,
        AlsAnimationSetDefinition set, AlsP5aAnimationRuntimeProfile actions)
    {
        using var selection = JsonDocument.Parse(selectionJson);
        foreach (var begin in new[] { true, false })
        {
            var name = begin ? "Received_NotifyBegin" : "Received_NotifyEnd";
            var text = selection.RootElement.GetProperty("graphs").EnumerateArray()
                .Single(g => g.GetProperty("path").GetString() == Source + ":" + name).GetProperty("nativeText").GetString()!;
            var graph = new Graph(text, true);
            var message = graph.One("K2Node_Message", "BPI_SetOverlayOverrideState");
            graph.Link(message, "execute", graph.One("K2Node_FunctionEntry", name), "then");
            var target = graph.Follow(message, "self").Item1;
            if (target.Member != "GetAnimInstance") throw new ArgumentException("Foreign Overlay override target.");
            if (begin)
            {
                var value = graph.Follow(message, "OverlayOverrideState").Item1;
                if (value.Kind != "K2Node_VariableGet" || value.Member != "OverlayOverrideState")
                    throw new ArgumentException("Overlay Begin must read its native instance property.");
            }
            else if (graph.Literal(message, "OverlayOverrideState") != "0")
                throw new ArgumentException("Overlay End must reset the native state to zero.");
        }
        using var notifies = JsonDocument.Parse(notifyJson);
        var values = new Dictionary<(int, int, int), int>();
        foreach (var asset in notifies.RootElement.GetProperty("montages").GetProperty("syncAssets").EnumerateArray())
        {
            var action = actions.Actions.Single(a => set.Montages[a.MontageId].ObjectPath == asset.GetProperty("path").GetString());
            foreach (var row in asset.GetProperty("notifies").EnumerateArray())
            {
                if (row.GetProperty("class").GetString() != Source + "_C") continue;
                var value = row.GetProperty("overlayOverrideState").GetInt32();
                if (value is < 0 or > 3) throw new ArgumentException("Unsupported native Overlay override state.");
                var timeline = set.Montages[action.MontageId].Timeline.Single(t => t.SourceIndex == row.GetProperty("index").GetInt32());
                values.Add((action.DefinitionId, action.MontageId, timeline.EventId), value);
            }
        }
        if (values.Count != 6) throw new ArgumentException("Incomplete default recovery Overlay override notify inventory.");
        return new(values);
    }
}

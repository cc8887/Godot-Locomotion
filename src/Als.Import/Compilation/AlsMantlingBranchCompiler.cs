using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

/// <summary>Explicit native ALS branching classes, not event tickMode or display-name guesses.
/// Sequence footsteps remain a separate queued timeline.</summary>
public static class AlsMantlingBranchCompiler
{
    public static AlsMantlingBranchingRuntime Compile(string json,AlsMantlingMontageProfile profile)
    {
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Require(profile.Poses.Values.All(p=>p.AnimationInputsDigest==digest),"Branching input belongs to another pose/montage resource version.");
        using var document=JsonDocument.Parse(json);var definitions=new List<AlsMantlingBranchDefinition>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var montage in document.RootElement.GetProperty("montages").EnumerateArray())
        {
            var path=Text(montage,"path");Require(seen.Add(path)&&profile.Definitions.ContainsKey(path),"Foreign branching montage.");
            var states=montage.GetProperty("notifies");Require(states.GetArrayLength()==2,"Unbound mantle branching state inventory.");
            var action=states[0];var early=states[1];
            Validate(action,0,"/Script/ALS.AlsAnimNotifyState_SetLocomotionAction");
            Validate(early,1,"/Script/ALS.AlsAnimNotifyState_EarlyBlendOut");
            var actionData=action.GetProperty("payload");var data=early.GetProperty("payload");
            definitions.Add(new(profile.Definitions[path].Asset.ActionDefinitionId,Number(action,"triggerTime"),Number(action,"endTriggerTime"),
                Number(early,"triggerTime"),Number(early,"endTriggerTime"),Number(data,"blend_out_duration"),
                data.GetProperty("check_input").GetBoolean(),data.GetProperty("check_locomotion_mode").GetBoolean(),
                data.GetProperty("check_rotation_mode").GetBoolean(),data.GetProperty("check_stance").GetBoolean(),
                Text(actionData,"locomotion_action"),Text(data,"locomotion_mode_equals"),Text(data,"rotation_mode_equals"),Text(data,"stance_equals")));
            void Validate(JsonElement state,int index,string type)
            {
                Require(state.GetProperty("index").GetInt32()==index&&Text(state,"class")==type&&Text(state,"notifyObject")==""&&
                    Text(state,"stateObject").StartsWith(path+":",StringComparison.Ordinal),"Unknown branching class or state identity.");
                Require(Number(state,"triggerTime")==Number(state,"time")+Number(state,"triggerOffset")&&
                    Number(state,"endTriggerTime")==Number(state,"triggerTime")+Number(state,"duration")+Number(state,"endTriggerOffset"),
                    "Branching trigger offsets differ from native float arithmetic.");
            }
        }
        Require(seen.SetEquals(profile.Definitions.Keys),"Incomplete mantle branching closure.");
        return new(definitions.ToArray());
    }
    private static string Text(JsonElement row,string name)=>row.GetProperty(name).GetString()??throw new ArgumentException(name);
    private static float Number(JsonElement row,string name){var v=row.GetProperty(name).GetSingle();Require(float.IsFinite(v),"Invalid branching number.");return v;}
    private static void Require(bool condition,string message){if(!condition)throw new ArgumentException(message);}
}

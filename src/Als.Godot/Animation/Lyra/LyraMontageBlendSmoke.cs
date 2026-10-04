using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using FileAccess = Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageBlendSmoke : Node
{
    private static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); }
    private static void Exact(float a, float b, string label)
    { Require(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b), $"{label}: {a:R}/{b:R}"); }
    public override void _Ready()
    { try { Run(); GetTree().Quit(); } catch (Exception e) { GD.PushError("Montage blend failed: " + e); GetTree().Quit(1); } }

    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var requests = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "montage_blend_v1_requests.json"));
        using var capture = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "montage_blend_v1_native.json"));
        var native = capture.RootElement; var input = requests.RootElement;
        Require(native.GetProperty("schemaVersion").GetInt32() == 1 && native.GetProperty("requestSha256").GetString() ==
            LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + "montage_blend_v1_requests.json")), "Stale blend capture.");
        foreach (var d in native.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + d.Name)), "Stale blend dependency.");
        var catalog = new LyraMontageCatalog(); var profiles = catalog.BlendProfiles ?? throw new Exception("Missing original blend profiles.");
        var mathCases = input.GetProperty("mathCases"); var mathNative = native.GetProperty("mathCases");
        Require(mathCases.GetArrayLength() == mathNative.GetArrayLength(), "Incomplete native profile math.");
        for (var i = 0; i < mathCases.GetArrayLength(); i++)
        {
            var q = mathCases[i]; var n = mathNative[i];
            var blend = new AlsMontageBlendSnapshot(q.GetProperty("alpha").GetSingle(), q.GetProperty("begin").GetSingle(),
                q.GetProperty("desired").GetSingle(), q.GetProperty("startAlpha").GetSingle(), (AlsActionBlendOption)q.GetProperty("option").GetInt32());
            Exact(blend.Alpha, n.GetProperty("alpha").GetSingle(), $"math/{i}/alpha");
            Exact(blend.BeginWeight, n.GetProperty("begin").GetSingle(), $"math/{i}/begin");
            Exact(AlsMontageBlendProfile.BoneWeight(q.GetProperty("factor").GetSingle(),
                (AlsMontageBlendProfileMode)q.GetProperty("mode").GetInt32(), blend, n.GetProperty("weight").GetSingle()),
                n.GetProperty("boneWeight").GetSingle(), $"math/{i}/bone");
        }
        var frames = 0; var frozenChecks = 0; var profileChecks = 0; var boneChecks = 0; var retry = 0; var dual = 0;
        foreach (var (trace, ti) in native.GetProperty("traces").EnumerateArray().Select((v, i) => (v, i)))
        {
            var runtime = catalog.CreateRuntime(); var source = input.GetProperty("traces")[ti];
            foreach (var (row, fi) in trace.GetProperty("frames").EnumerateArray().Select((v, i) => (v, i)))
            {
                var identity = new AlsFrameIdentity(fi, (uint)(81 + ti), 31); var frame = source.GetProperty("frames")[fi];
                string? first = null;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    runtime.Begin(identity, frame.GetProperty("delta").GetSingle());
                    var entries = runtime.Evaluation.ToArray(); var physical = entries.DistinctBy(e => e.InstanceId).ToArray();
                    var frozen = row.GetProperty("frozen");
                    Require(physical.Length == frozen.GetArrayLength(), $"{ti}/{fi}/physical count");
                    for (var i = 0; i < physical.Length; i++)
                    {
                        var e = physical[i]; var n = frozen[i]; var b = e.BlendSnapshot;
                        var label = $"{ti}/{fi}/{i}";
                        Require(e.ActionDefinitionId == n.GetProperty("asset").GetInt32(), label + "/asset");
                        Exact(e.MontagePosition, n.GetProperty("position").GetSingle(), label + "/position");
                        Exact(e.Weight, n.GetProperty("weight").GetSingle(), label + "/weight");
                        Exact(b.Alpha, n.GetProperty("alpha").GetSingle(), label + "/alpha");
                        Exact(b.BeginWeight, n.GetProperty("begin").GetSingle(), label + "/begin");
                        Exact(b.DesiredWeight, n.GetProperty("desired").GetSingle(), label + "/desired");
                        Exact(b.StartAlpha, n.GetProperty("startAlpha").GetSingle(), label + "/startAlpha");
                        Require((int)b.Option == n.GetProperty("option").GetInt32() && b.ProfileId == n.GetProperty("profile").GetInt32(), label + "/policy");
                        var tracks = entries.Where(v => v.InstanceId == e.InstanceId).ToArray();
                        foreach (var t in tracks) Require(t.BlendSnapshot == b && t.MontagePosition == e.MontagePosition, label + "/shared track blend");
                        if (attempt == 1) { frozenChecks++; if (tracks.Length > 1) dual++; }
                        if (b.ProfileId < 0) continue;
                        var profile = profiles.Profiles[b.ProfileId]; var weights = n.GetProperty("boneWeights");
                        Require(weights.GetArrayLength() == profile.Factors.Length, label + "/target bones");
                        for (var bone = 0; bone < profile.Factors.Length; bone++)
                        {
                            Exact(AlsMontageBlendProfile.BoneWeight(profile.Factors[bone], profile.Mode, b, e.Weight),
                                weights[bone].GetSingle(), label + $"/bone{bone}");
                            if (attempt == 1) boneChecks++;
                        }
                        if (attempt == 1) profileChecks++;
                    }
                    foreach (var q in frame.GetProperty("commands").EnumerateArray())
                    {
                        var asset = q.GetProperty("asset").GetInt32();
                        if (q.GetProperty("stop").GetBoolean())
                        {
                            var instanceStop = q.TryGetProperty("instanceStop", out var explicitStop) && explicitStop.GetBoolean();
                            var active = runtime.Candidate.ToArray().LastOrDefault(v => v.MontageId == asset && (instanceStop || v.OwnsActiveActionLookup));
                            if (active.InstanceId > 0) Require(runtime.StopInstance(active.InstanceId, q.GetProperty("blend").GetSingle(),
                                catalog.Definitions[asset].Lifecycle.BlendOutOption), "Active stop rejected.");
                        }
                        else Require(runtime.PlayAction(asset, q.GetProperty("rate").GetSingle(), q.GetProperty("start").GetSingle(),
                            stopGroup: q.GetProperty("stopGroup").GetBoolean()), "Play rejected.");
                    }
                    Require(entries.SequenceEqual(runtime.Evaluation.ToArray()), "Commands mutated frozen blends.");
                    var signature = JsonSerializer.Serialize(runtime.Candidate.ToArray());
                    if (attempt == 0) { first = signature; runtime.Discard(); retry++; }
                    else { Require(first == signature, "Discard changed physical blend history."); runtime.Commit(identity); }
                }
                frames++;
            }
        }
        Require(frames == 13440 && retry == frames && dual > 0 &&
            frozenChecks == native.GetProperty("counts").GetProperty("frozen").GetInt32() &&
            profileChecks == native.GetProperty("counts").GetProperty("profileFrames").GetInt32() &&
            boneChecks == native.GetProperty("counts").GetProperty("boneWeights").GetInt32(), "Incomplete blend history coverage.");
        GD.Print($"LYRA_MONTAGE_BLEND_GODOT_OK frames={frames} frozen={frozenChecks} profiles={profileChecks} bones={boneChecks} math={mathCases.GetArrayLength()} retry={retry} dual={dual} exact=true slotPose=false production=false");
    }
}

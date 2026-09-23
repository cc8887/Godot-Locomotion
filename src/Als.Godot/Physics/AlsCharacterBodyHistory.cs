using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using GodotAls.Locomotion;

namespace GodotAls.Physics;

// Runs after the ordinary animation Main commit. Physical history has its own
// step identity: animation retries can leave the source frame unchanged while
// another physical step consumes the kinematic Reset mode.
public partial class AlsCharacterBodyHistory : Node
{
    private AlsP3Character _owner = null!;
    private AlsCorePhysicsPose _bridge = null!;
    private AlsLocalPose[] _pose = [];
    private AlsIslandBodyState[] _seed = [];
    private AlsPrecisePose[] _actors = [];
    private string[] _names = [];
    private AlsKinematicBodyHistory? _history;
    private bool _teleport;
    private long _physicalSequence;
    internal AlsFrameIdentity SourceAnimationIdentity { get; private set; }
    internal AlsFrameIdentity PhysicsIdentity => _history?.CommittedIdentity ?? default;
    internal int BodyCount => _seed.Length;
    internal string? Failure { get; private set; }

    internal void Configure(AlsP3Character owner, AlsP3RuntimeContext context)
    {
        _owner = owner;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Lifecycle;
        var skeleton = context.AnimationSet.Skeletons[context.Profile.SkeletonId];
        var mesh = context.AnimationSet.SkeletalMeshes[context.Profile.MannequinMeshId];
        var definition = AlsPhysicsAssetCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), mesh.ObjectPath);
        _names = skeleton.LogicalBones.Select(b => b.Name).ToArray();
        _bridge = new(definition, _names,
            skeleton.LogicalBones.Select(b => b.ParentLogicalId).ToArray(), owner.Handle.CharacterId, owner.Handle.Generation);
        _pose = new AlsLocalPose[skeleton.LogicalBones.Length];
        _seed = new AlsIslandBodyState[definition.Bodies.Length];
        _actors = new AlsPrecisePose[_seed.Length];
    }

    // Scheduling gaps are not a continuous physical history. Reinitialize from
    // the first accepted visible pose on resume instead of differencing the gap.
    internal void ResetHistory()
    {
        Main(); _history = null; SourceAnimationIdentity = default; Failure = null; _teleport = false;
    }

    internal void MarkTeleport() { Main(); _teleport = true; }

    internal AlsFrameIdentity CopyCompleted(Span<AlsIslandBodyState> destination)
    {
        Main();
        if (_history is null || Failure is not null) throw new InvalidOperationException("No completed character body history.");
        _history.CopyCommitted(_history.CommittedIdentity, destination);
        return _history.CommittedIdentity;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Failure is not null || !_owner.BodyHistoryActive) return;
        var diagnostics = _owner.Diagnostics;
        if (diagnostics.Identity.FrameId <= 0 || diagnostics.PresentationPending) return;
        try
        {
            var fresh = diagnostics.Identity != SourceAnimationIdentity;
            if (fresh)
            {
                var entry = _owner.CopyCommittedRagdollEntry(diagnostics.Identity, _pose);
                _bridge.Seed(entry.Identity, entry.SkeletonToWorld, _pose, Vector3.Zero, Vector3.Zero, _seed);
                for (var i = 0; i < _actors.Length; i++) _actors[i] = _seed[i].Actor;
                if (_history is null)
                {
                    _history = new(Next(), _actors);
                    _physicalSequence = _history.CommittedIdentity.FrameId;
                }
                else
                {
                    var next = Next();
                    _history.PrepareTargets(next, _actors, AlsPhysicsStepTime.FromEngineSeconds(delta), _teleport);
                    _history.Commit(next);
                    _physicalSequence = next.FrameId;
                }
                SourceAnimationIdentity = entry.Identity; _teleport = false;
            }
            else if (_history is not null)
            {
                var next = Next(); _history.PrepareNoTarget(next); _history.Commit(next); _physicalSequence = next.FrameId;
            }
        }
        catch (Exception error)
        {
            _history?.Cancel(); Failure = error.Message;
            var bad = string.Join(";", _pose.Select((p, i) => (p, i)).Where(x =>
                System.Numerics.Vector3.Distance(x.p.Scale, System.Numerics.Vector3.One) > 1e-6f ||
                MathF.Abs(x.p.Rotation.LengthSquared() - 1) > 1e-6f).Take(8).Select(x =>
                $"bone={_names[x.i]} scale={x.p.Scale} qlen2={x.p.Rotation.LengthSquared():R}"));
            GD.PushError($"ALS_BODY_HISTORY_FAILED animation={diagnostics.Identity.FrameId} {bad} " + error);
        }
    }

    private AlsFrameIdentity Next() => new(checked(_physicalSequence + 1),
        _owner.Handle.CharacterId, _owner.Handle.Generation);
    private static void Main()
    { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Character body history requires Main."); }
}

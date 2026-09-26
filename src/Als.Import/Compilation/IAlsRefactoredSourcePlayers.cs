using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

/// <summary>A graph-local player address space. A shared view owns no clocks,
/// filters or poses; only the outer Sync scope advances and publishes them.</summary>
public interface IAlsRefactoredSourcePlayers
{
    bool Deferred => false;
    string CatalogDigest { get; }
    string Source(int player);
    ReadOnlySpan<string> BoneNames(int player);
    ReadOnlySpan<string> CurveNames(int player);
    ReadOnlySpan<AlsAssetSyncPlayer> Ticks { get; }
    ReadOnlySpan<AlsAssetPlayerHistory> Players { get; }
    float CommittedTime(int player);
    Vector2 FilteredInput(int player);
    void BeginRegistration(bool initialize) { }
    void Register(in AlsRefactoredSourcePlayerInput input, in AlsPoseUpdateContext context) { }
    void Prepare(long frame, ReadOnlySpan<AlsRefactoredSourcePlayerInput> inputs, float delta,
        bool reinitializeInstance = false, ReadOnlySpan<int> groupOrder = default);
    void Evaluate(long frame, int player);
    ReadOnlySpan<AlsPrecisePose> Pose(int player);
    ReadOnlySpan<AlsInertialCurve> Curves(int player);
    void ValidateCommit(long frame);
    void Commit(long frame);
    void Cancel();
}

internal sealed class AlsRefactoredSourcePlayerView : IAlsRefactoredSourcePlayers
{
    private readonly AlsRefactoredSourceSyncScope _scope;
    private readonly AlsRefactoredSourcePlayerRuntime _bank;
    private readonly uint _owner;
    private readonly AlsAssetSyncPlayer[] _ticks;
    private readonly AlsAssetPlayerHistory[] _players;
    private long _frame;
    private int _count;
    private bool _prepared, _captured;
    public bool Deferred => true;
    public string CatalogDigest => _bank.CatalogDigest;
    internal AlsRefactoredSourcePlayerView(AlsRefactoredSourceSyncScope scope,
        AlsRefactoredSourcePlayerRuntime bank, uint owner, int count)
    { _scope = scope; _bank = bank; _owner = owner; _ticks = new AlsAssetSyncPlayer[count]; _players = new AlsAssetPlayerHistory[count]; }
    private int Id(int local) => _scope.PlayerId(_owner, local);
    public string Source(int player) => _bank.Source(Id(player));
    public ReadOnlySpan<string> BoneNames(int player) => _bank.BoneNames(Id(player));
    public ReadOnlySpan<string> CurveNames(int player) => _bank.CurveNames(Id(player));
    public float CommittedTime(int player) => _bank.CommittedTime(Id(player));
    public Vector2 FilteredInput(int player) { ValidateCommit(_frame); return _bank.FilteredInput(Id(player)); }
    public void BeginRegistration(bool initialize) { if (initialize) _scope.ReinitializeOwner(_owner); }
    public void Register(in AlsRefactoredSourcePlayerInput input, in AlsPoseUpdateContext context) => _scope.Enqueue(_owner, input, context);
    public void Prepare(long frame, ReadOnlySpan<AlsRefactoredSourcePlayerInput> inputs, float delta,
        bool reinitializeInstance = false, ReadOnlySpan<int> groupOrder = default)
    {
        if (_prepared || !groupOrder.IsEmpty) throw new InvalidOperationException("Shared player view already staged or supplied private group order.");
        _scope.ValidateOwnerInputs(_owner, frame, delta, inputs);
        _frame = frame; _count = inputs.Length; _prepared = true; _captured = false;
    }
    // These are local player IDs with shared-bank sample offsets. No copied
    // sample clock or per-owner Sync group is exposed as an independent bank.
    public ReadOnlySpan<AlsAssetSyncPlayer> Ticks { get { Capture(); return _ticks.AsSpan(0, _count); } }
    public ReadOnlySpan<AlsAssetPlayerHistory> Players { get { Capture(); return _players.AsSpan(0, _count); } }
    private void Capture()
    {
        ValidateCommit(_frame); if (_captured) return;
        var t = 0; var p = 0;
        foreach (var tick in _bank.Ticks)
        { var id = _scope.OwnerPlayer(tick.PlayerId); if (id.Owner == _owner) _ticks[t++] = tick with { PlayerId = id.LocalPlayer }; }
        foreach (var player in _bank.Players)
        { var id = _scope.OwnerPlayer(player.PlayerId); if (id.Owner == _owner) _players[p++] = player with { PlayerId = id.LocalPlayer }; }
        if (t != _count || p != _count) throw new InvalidOperationException("Shared source view membership differs.");
        _captured = true;
    }
    public void Evaluate(long frame, int player) { ValidateCommit(frame); _bank.Evaluate(frame, Id(player)); }
    public ReadOnlySpan<AlsPrecisePose> Pose(int player) { ValidateCommit(_frame); return _bank.Pose(Id(player)); }
    public ReadOnlySpan<AlsInertialCurve> Curves(int player) { ValidateCommit(_frame); return _bank.Curves(Id(player)); }
    public void ValidateCommit(long frame)
    {
        if (!_prepared || frame != _frame) throw new ArgumentException("Shared source view is not staged.");
        _scope.ValidateFrame(frame);
    }
    public void Commit(long frame) { ValidateCommit(frame); Cancel(); }
    public void Cancel() { _prepared = _captured = false; }
}

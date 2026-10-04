using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRigAlphaState(float Result, float Interpolated, bool Initialized);

// Separate private history per actual VM instruction, including both collapsed
// foot subgraphs. This object is cloned with its enclosing Rig candidate.
internal sealed class LyraFootPlantRigDynamics
{
    private static readonly AlsOverlayAlphaPolicy AlphaPolicy = new(1, 0, false, 0, 1, true, 5, 5);
    private readonly AlsRigVectorSpringState[] _vectors = new AlsRigVectorSpringState[2];
    private readonly AlsFloatSpringResult[] _scalars = new AlsFloatSpringResult[3];
    private readonly LyraRigAlphaState[] _alphas = new LyraRigAlphaState[2];
    public LyraFootPlantRigDynamics(JsonElement program)
    {
        var rows = program.GetProperty("instructions"); var literals = program.GetProperty("literal");
        foreach (var (index, type) in new[] { (242, "FRigUnit_SpringInterpVectorV2"), (299, "FRigUnit_SpringInterpVectorV2"),
            (311, "FRigUnit_SpringInterpV2"), (348, "FRigUnit_SpringInterpV2"), (371, "FRigUnit_SpringInterpV2") })
        {
            var row = rows[index]; var o = row.GetProperty("operands");
            JsonElement Value(int i) => literals.GetProperty(o[i].GetProperty("name").GetString()!);
            if (!row.GetProperty("text").GetString()!.Contains(type + "::Execute") || o.GetArrayLength() != 12 ||
                Value(1).GetSingle() != (index < 300 ? 8f : 2.5f) || Value(2).GetSingle() != 1 ||
                !Value(4).GetBoolean() || Value(6).GetSingle() != (index < 300 ? 0 : .2f) || Value(7).GetBoolean())
                throw new NotSupportedException("Changed FootPlant spring instruction: " + index);
            var force = Value(3);
            if (index < 300 ? force.EnumerateArray().Any(v => v.GetDouble() != 0) : force.GetSingle() != 0)
                throw new NotSupportedException("Changed FootPlant spring force.");
        }
        foreach (int index in new[] { 317, 326 })
        {
            var o = rows[index].GetProperty("operands"); JsonElement Value(int i) => literals.GetProperty(o[i].GetProperty("name").GetString()!);
            if (!rows[index].GetProperty("text").GetString()!.Contains("FRigVMFunction_AlphaInterp::Execute") || o.GetArrayLength() != 14 ||
                Value(1).GetSingle() != 1 || Value(2).GetSingle() != 0 || Value(3).GetBoolean() || Value(6).GetBoolean() ||
                !Value(9).GetBoolean() || Value(10).GetSingle() != 5 || Value(11).GetSingle() != 5)
                throw new NotSupportedException("Changed FootPlant AlphaInterp instruction.");
        }
    }
    private LyraFootPlantRigDynamics() { }
    public LyraFootPlantRigDynamics Clone()
    { var result = new LyraFootPlantRigDynamics(); result.CopyFrom(this); return result; }
    public void CopyFrom(LyraFootPlantRigDynamics source)
    { source._vectors.CopyTo(_vectors, 0); source._scalars.CopyTo(_scalars, 0); source._alphas.CopyTo(_alphas, 0); }
    public void Reset() { Array.Clear(_vectors); Array.Clear(_scalars); Array.Clear(_alphas); }
    public AlsRigVectorSpringState Vector(int owner) => owner is >= 0 and < 2 ? _vectors[owner] : throw new ArgumentOutOfRangeException(nameof(owner));
    // The native VectorV2 body updates SpringState.Velocity but never assigns
    // its distinct output Velocity pin. The original register starts at zero.
    public AlsDoubleVector VectorOutputVelocity(int owner) { _ = Vector(owner); return default; }
    public AlsFloatSpringResult Scalar(int owner) => owner is >= 2 and < 5 ? _scalars[owner - 2] : throw new ArgumentOutOfRangeException(nameof(owner));
    public LyraRigAlphaState Alpha(int owner) => owner is >= 5 and < 7 ? _alphas[owner - 5] : throw new ArgumentOutOfRangeException(nameof(owner));
    public AlsDoubleVector ExecuteVector(int owner, double delta, AlsDoubleVector target, AlsDoubleVector current)
    {
        _ = Vector(owner);
        var next = AlsRigVectorSpringModel.Evaluate(_vectors[owner], current, target, delta, 8, 0);
        _vectors[owner] = next; return next.Result;
    }
    public float ExecuteScalar(int owner, double delta, float target, float current = 0)
    {
        if (owner is < 2 or > 6 || !double.IsFinite(delta) || delta < 0 || !float.IsFinite((float)delta) ||
            !float.IsFinite(target) || !float.IsFinite(current)) throw new ArgumentException("Invalid Rig dynamics call.");
        if (owner < 5)
        {
            var i = owner - 2; float angular = 2.5f * 2f * MathF.PI;
            var result = AlsKismetFloatSpring.Evaluate(current, target, _scalars[i].State, angular * angular, 1, (float)delta, 1, .2f);
            _scalars[i] = result; return result.Value;
        }
        var old = _alphas[owner - 5];
        var initialized = old.Initialized; var history = old.Interpolated;
        var value = AlsInputScaleBiasClamp.Apply(target, AlphaPolicy, (float)delta, ref initialized, ref history);
        _alphas[owner - 5] = new(value, history, initialized); return value;
    }
}

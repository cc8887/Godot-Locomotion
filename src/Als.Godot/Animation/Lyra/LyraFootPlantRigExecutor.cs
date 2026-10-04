using System.Text.Json;
using GodotAls.Core.Animation;
using LyraRigOperand = GodotAls.Core.Animation.AlsRigOperand;
using LyraRigInstruction = GodotAls.Core.Animation.AlsRigInstruction;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRigSweepRequest(int Instruction, AlsDoubleVector Start,
    AlsDoubleVector End, int TraceChannel, float Radius);
internal readonly record struct LyraRigSweepHit(bool Hit, AlsDoubleVector Position, AlsDoubleVector Normal);
internal interface ILyraFootPlantRigCollision
{
    // Rig component space in centimeters. The provider owns the world-space
    // conversion, trace mask, ignored character and physics query lifetime.
    LyraRigSweepHit Sweep(in LyraRigSweepRequest request);
}
internal readonly record struct LyraRigIkDiagnostic(int Instruction, AlsPrecisePose A, AlsPrecisePose B,
    AlsPrecisePose C, AlsDoubleVector Pole, AlsDoubleVector Primary, AlsDoubleVector Secondary,
    float LengthA, float LengthB, float StretchStart, float StretchMaximum,
    AlsPrecisePose SolvedA, AlsPrecisePose SolvedB, AlsPrecisePose SolvedC);

// Original fixed FootPlant units, connected by typed immediate registers.
// Collision is an explicit dependency; no native Euler/pose answer is read.
// Construct/clone this entire owner for a candidate and publish only on commit.
internal sealed class LyraFootPlantRigExecutor : IAlsRigInstructionExecutor
{
    private readonly LyraFootPlantRigMemory _initial;
    private readonly LyraFootPlantRigHierarchy _hierarchy;
    private LyraFootPlantRigDynamics _dynamics;
    public LyraFootPlantRigMemory Memory { get; private set; }
    public LyraFootPlantRigHierarchy Hierarchy => _hierarchy;
    public LyraFootPlantRigDynamics Dynamics => _dynamics;
    private readonly Dictionary<int, LyraRigSweepHit> _sweeps = new();
    private ILyraFootPlantRigCollision? _collision;
    private double _delta;
    internal Action<LyraRigIkDiagnostic>? ObserveIk { get; set; }
    public LyraFootPlantRigExecutor(JsonElement program, JsonElement graph, LyraFootPlantRigHierarchy hierarchy)
    {
        Memory = new(program, graph); _initial = Memory.Clone(); _hierarchy = hierarchy;
        _dynamics = new(program);
    }
    private LyraFootPlantRigExecutor(LyraFootPlantRigExecutor source)
    {
        Memory = source.Memory.Clone(); _initial = source._initial;
        _hierarchy = source._hierarchy.Clone(); _dynamics = source._dynamics.Clone();
    }
    public LyraFootPlantRigExecutor Clone() => new(this);
    public void Reset()
    { Memory.ResetWork(_initial); _hierarchy.Reset(); _dynamics.Reset(); _sweeps.Clear(); }
    public void BeginExecution(double delta, ILyraFootPlantRigCollision? collision)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid Rig delta.");
        _delta = delta; _collision = collision; _sweeps.Clear();
    }
    public void Copy(LyraRigOperand source, LyraRigOperand target) => Memory.Write(target, Memory.Read(source));
    public void Zero(LyraRigOperand operand) => Memory.Write(operand, "None");
    public bool ReadBool(LyraRigOperand operand) => (bool)Memory.Read(operand);
    public string ReadName(LyraRigOperand operand) => (string)Memory.Read(operand);
    public void WriteName(LyraRigOperand operand, string value) => Memory.Write(operand, value);
    public void Execute(LyraRigInstruction instruction)
    {
        var o = instruction.Operands;
        object R(int i) => Memory.Read(o[i]);
        float F(int i) => Convert.ToSingle(R(i));
        AlsDoubleVector V(int i) => (AlsDoubleVector)R(i);
        AlsPrecisePose P(int i) => (AlsPrecisePose)R(i);
        bool B(int i) => (bool)R(i);
        string Name(int i) => ((JsonElement)R(i)).GetProperty("Name").GetString()!;
        bool Local(int i) => (string)R(i) == "LocalSpace";
        void W(int i, object value) => Memory.Write(o[i], value);
        if (instruction.Function.StartsWith("DISPATCH_RigVMDispatch_If::", StringComparison.Ordinal))
        { W(3, R(B(0) ? 1 : 2)); return; }
        switch (instruction.Function)
        {
            case "FRigUnit_BeginExecution::Execute":
            case "FRigUnit_PrepareForExecution::Execute":
            case "FRigVMFunction_Sequence::Execute":
            case "FRigVMFunction_DebugRectangleNoSpace::Execute":
            case "FRigVMFunction_DebugLineNoSpace::Execute":
            case "FRigVMFunction_DebugTransformMutableNoSpace::Execute":
            case "FRigVMFunction_VisualDebugVectorNoSpace::Execute": return;
            case "FRigVMFunction_MathBoolNot::Execute": W(1, !B(0)); return;
            case "FRigVMFunction_MathBoolAnd::Execute": W(2, B(0) && B(1)); return;
            case "FRigVMFunction_MathVectorIsNearlyZero::Execute": W(2, V(0).NearlyZero(Math.Max(F(1), 1e-8f))); return;
            case "FRigVMFunction_MathVectorAdd::Execute": W(2, V(0) + V(1)); return;
            case "FRigVMFunction_MathVectorSub::Execute": W(2, V(0) - V(1)); return;
            case "FRigVMFunction_MathVectorScale::Execute": W(2, V(0) * F(1)); return;
            case "FRigVMFunction_MathVectorUnit::Execute":
                var unit = V(0); W(1, unit.NearlyZero(1e-4) ? default(AlsDoubleVector) : unit.SafeNormal(1e-8)); return;
            case "FRigVMFunction_MathVectorLength::Execute": W(1, (float)Math.Sqrt(V(0).LengthSquared)); return;
            case "FRigVMFunction_MathFloatAdd::Execute": W(2, F(0) + F(1)); return;
            case "FRigVMFunction_MathFloatSub::Execute": W(2, F(0) - F(1)); return;
            case "FRigVMFunction_MathFloatMin::Execute": W(2, Math.Min(F(0), F(1))); return;
            case "FRigVMFunction_MathFloatRemap::Execute":
                W(6, GodotAls.Core.Math.AlsMath.Remap(F(0), F(1), F(2), F(3), F(4), B(5))); return;
            case "FRigVMFunction_MathQuaternionMul::Execute": W(2, (AlsQuaternion)R(0) * (AlsQuaternion)R(1)); return;
            case "FRigVMFunction_MathQuaternionToEuler::Execute":
                if ((string)R(1) != "ZYX") throw new NotSupportedException("Changed FootPlant Euler order.");
                W(2, LyraFootPlantRigMath.EulerZYX((AlsQuaternion)R(0))); return;
            case "FRigVMFunction_MathTransformInverse::Execute": W(1, LyraFootPlantRigMath.Inverse(P(0))); return;
            case "FRigVMFunction_MathTransformRotateVector::Execute": W(2, (P(0).Scale * V(1)).Rotate(P(0).Rotation)); return;
            case "FRigUnit_GetTransform::Execute": W(3, _hierarchy.Get(Name(0), Local(1), B(2))); return;
            case "FRigUnit_GetRelativeTransformForItem::Execute":
                W(4, AlsPrecisePose.Relative(_hierarchy.Get(Name(0), initial: B(1)), _hierarchy.Get(Name(2), initial: B(3))).Normalized()); return;
            case "FRigUnit_SetTransform::Execute":
                RequireOne(F(4)); _hierarchy.Set(Name(0), P(3), Local(1), B(2), children: B(5)); return;
            case "FRigUnit_SetRotation::Execute":
                RequireOne(F(4)); var old = _hierarchy.Get(Name(0), Local(1), B(2));
                _hierarchy.Set(Name(0), old with { Rotation = (AlsQuaternion)R(3) }, Local(1), B(2), children: B(5)); return;
            case "FRigUnit_SetControlOffset::Execute":
                string control = (string)R(0);
                _hierarchy.Set(control, P(1), Local(2), offset: true);
                _hierarchy.Set(control, P(1), Local(2), initial: true, offset: true); return;
            case "FRigUnit_OffsetTransformForItem::Execute":
                RequireOne(F(2)); string item = Name(0); bool local = _hierarchy.IsGlobalDirty(item);
                var previous = _hierarchy.Get(item, local);
                _hierarchy.Set(item, AlsPrecisePose.Compose(P(1), previous).Normalized(), local, children: B(3)); return;
            case "FRigUnit_ParentConstraint::Execute":
                RequireOne(F(5)); ParentConstraint(Name(0), (JsonElement)R(2), (JsonElement)R(3), (JsonElement)R(4)); return;
            case "FRigUnit_AimBoneMath::Execute":
                W(4, LyraFootPlantRigMath.Aim(P(0), (LyraRigAimTarget)R(1), (LyraRigAimTarget)R(2), F(3))); return;
            case "FRigUnit_SphereTraceByTraceChannel::Execute":
                if (!_sweeps.TryGetValue(instruction.Index, out var hit))
                {
                    var request = new LyraRigSweepRequest(instruction.Index, V(0), V(1), Convert.ToInt32(R(2)), F(3));
                    hit = _collision?.Sweep(request) ?? new(false, default, new(0, 0, 1));
                    _sweeps.Add(instruction.Index, hit);
                }
                W(4, hit.Hit); W(5, hit.Position); W(6, hit.Normal); return;
            case "FRigUnit_SpringInterpVectorV2::Execute":
                int vectorOwner = instruction.Index == 242 ? 0 : instruction.Index == 299 ? 1 : throw new NotSupportedException("Unknown Rig vector spring.");
                W(8, _dynamics.ExecuteVector(vectorOwner, _delta, V(0), V(5))); W(9, _dynamics.VectorOutputVelocity(vectorOwner)); return;
            case "FRigUnit_SpringInterpV2::Execute":
                int scalarOwner = instruction.Index switch { 311 => 2, 348 => 3, 371 => 4, _ => throw new NotSupportedException("Unknown Rig float spring.") };
                W(8, _dynamics.ExecuteScalar(scalarOwner, _delta, F(0), F(5))); W(9, _dynamics.Scalar(scalarOwner).State.Velocity); return;
            case "FRigVMFunction_AlphaInterp::Execute":
                int alphaOwner = instruction.Index == 317 ? 5 : instruction.Index == 326 ? 6 : throw new NotSupportedException("Unknown Rig alpha.");
                W(12, _dynamics.ExecuteScalar(alphaOwner, _delta, F(0))); return;
            case "FRigUnit_TwoBoneIKSimplePerItem::Execute":
                RequireOne(F(6)); RequireOne(F(13));
                if ((string)R(8) != "Location" || Name(9) != "None" || F(14) <= 1e-8f || F(15) <= 1e-8f)
                    throw new NotSupportedException("Changed original FootPlant IK configuration.");
                var a = _hierarchy.Get(Name(0)); var b = a with { Position = _hierarchy.Get(Name(1)).Position }; var c = P(3);
                var inputA = a; var inputB = b; var inputC = c;
                LyraFootPlantRigMath.SolveIk(ref a, ref b, ref c, V(7), V(4), V(5), F(14), F(15), B(10), F(11), F(12));
                ObserveIk?.Invoke(new(instruction.Index, inputA, inputB, inputC, V(7), V(4), V(5), F(14), F(15), F(11), F(12), a, b, c));
                _hierarchy.Set(Name(0), a, children: B(16)); _hierarchy.Set(Name(1), b, children: B(16));
                _hierarchy.Set(Name(2), c, children: B(16)); return;
            default: throw new NotSupportedException("Unimplemented original Rig unit: " + instruction.Function);
        }
    }
    private static void RequireOne(float weight)
    { if (weight != 1) throw new NotSupportedException("Changed original FootPlant unit weight."); }
    private void ParentConstraint(string child, JsonElement filter, JsonElement parents, JsonElement advanced)
    {
        if (parents.GetArrayLength() != 1 || parents[0].GetProperty("Weight").GetSingle() != 1 ||
            advanced.GetProperty("InterpolationType").GetString() != "Average" ||
            filter.EnumerateObject().Any(v => v.Value.EnumerateObject().Any(axis => !axis.Value.GetBoolean())))
            throw new NotSupportedException("Changed original knee parent constraint.");
        string parent = parents[0].GetProperty("Item").GetProperty("Name").GetString()!;
        _hierarchy.ApplySingleParentConstraint(child, parent);
    }
}

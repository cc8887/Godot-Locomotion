using System.Text.Json;
using GodotAls.Core.Animation;
using LyraRigOperand = GodotAls.Core.Animation.AlsRigOperand;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRigAimTarget(float Weight, AlsDoubleVector Axis,
    AlsDoubleVector Target, string Kind, string Space) : IAlsRigStructValue
{
    public object ReadField(string name) => name == "Target" ? Target : throw new NotSupportedException("Unknown AimTarget field.");
    public IAlsRigStructValue WithField(string name, object value) => name == "Target" ?
        this with { Target = (AlsDoubleVector)value } : throw new NotSupportedException("Unknown AimTarget field.");
}

// Resource types and JSON conversion remain here; Core owns registers and paths.
internal sealed class LyraFootPlantRigMemory
{
    private readonly AlsRigMemory _core;
    private readonly Dictionary<(int Memory, string Name), string> _types = new();
    public LyraFootPlantRigMemory(JsonElement program, JsonElement graph)
    {
        foreach (var v in program.GetProperty("workTypes").EnumerateObject()) _types[(0, v.Name)] = v.Value.GetString()!;
        foreach (var v in graph.GetProperty("variables").EnumerateArray())
            _types[(2, v.GetProperty("name").GetString()!)] = v.GetProperty("type").GetString()!;
        var registers = new List<AlsRigRegister>();
        void Add(int memory, JsonElement values)
        {
            foreach (var v in values.EnumerateObject())
            {
                var value = Parse(v.Value, _types.GetValueOrDefault((memory, v.Name)));
                registers.Add(new(memory, v.Name, value.GetType(), value));
            }
        }
        Add(0, program.GetProperty("initial").GetProperty("work"));
        Add(1, program.GetProperty("literal"));
        Add(2, program.GetProperty("initial").GetProperty("variables"));
        _core = new(registers);
    }
    private LyraFootPlantRigMemory(LyraFootPlantRigMemory source)
    { _core = source._core.Clone(); _types = source._types; }
    public LyraFootPlantRigMemory Clone() => new(this);
    public void ResetWork(LyraFootPlantRigMemory initial) => _core.ResetWork(initial._core);
    internal static AlsDoubleVector Vector(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
    internal static object Parse(JsonElement v, string? type = null)
    {
        if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) return v.GetBoolean();
        if (v.ValueKind == JsonValueKind.Number) return type == "float" ? (object)v.GetSingle() : v.GetDouble();
        if (v.ValueKind == JsonValueKind.String) return v.GetString()!;
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 && v[0].ValueKind == JsonValueKind.Number)
        {
            if (v.GetArrayLength() == 3) return Vector(v);
            if (v.GetArrayLength() == 4) return new AlsQuaternion(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble(), v[3].GetDouble());
        }
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("p", out _)) return LyraFootPlantRigConstruction.Pose(v);
        if (type == "FRigUnit_AimItem_Target" || v.ValueKind == JsonValueKind.Object && v.TryGetProperty("Kind", out _) && v.TryGetProperty("Axis", out _))
            return new LyraRigAimTarget(v.GetProperty("Weight").GetSingle(),
            Vector(v.GetProperty("Axis")), Vector(v.GetProperty("Target")), v.GetProperty("Kind").GetString()!,
            v.GetProperty("Space").GetProperty("Name").GetString()!);
        return v.Clone();
    }
    public object Read(int memory, string name) => _core.Read(memory, name);
    public object Read(LyraRigOperand o) => _core.Read(o.Memory, o.Name, o.Path);
    public void Write(int memory, string name, object value) => Write(new(memory, -1, -1, name, ""), value);
    public void Write(LyraRigOperand o, object value)
    {
        if (o.Path.Length == 0 && value is JsonElement element)
            value = Parse(element, _types.GetValueOrDefault((o.Memory, o.Name)));
        _core.Write(o.Memory, o.Name, value, o.Path);
    }
}

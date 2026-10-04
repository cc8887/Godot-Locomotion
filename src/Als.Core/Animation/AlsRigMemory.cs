using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Animation;

// Extensions must be immutable values. Replacing a field returns a new value,
// so register snapshots can share boxed values without sharing writable state.
public interface IAlsRigStructValue
{
    object ReadField(string name);
    IAlsRigStructValue WithField(string name, object value);
}

public readonly record struct AlsRigRegister(int Memory, string Name, Type ValueType, object InitialValue);

public sealed class AlsRigMemory
{
    private readonly Dictionary<(int Memory, string Name), Type> _types;
    private readonly Dictionary<(int Memory, string Name), object> _values;

    public AlsRigMemory(IEnumerable<AlsRigRegister> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);
        _types = new(); _values = new();
        foreach (var r in registers)
        {
            if (r.Memory is < 0 or > 2 || string.IsNullOrWhiteSpace(r.Name) || r.ValueType is null ||
                !Supported(r.ValueType) || !_types.TryAdd((r.Memory, r.Name), r.ValueType))
                throw new ArgumentException("Invalid or duplicate Rig register.");
            _values.Add((r.Memory, r.Name), Normalize(r.ValueType, r.InitialValue));
        }
    }

    private AlsRigMemory(AlsRigMemory source)
    { _types = source._types; _values = new(source._values); }
    public AlsRigMemory Clone() => new(this);

    public void ResetWork(AlsRigMemory initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (!ReferenceEquals(_types, initial._types)) throw new ArgumentException("Foreign Rig register layout.");
        foreach (var (key, value) in initial._values)
            if (key.Memory == 0) _values[key] = value;
    }

    public object Read(int memory, string name, string path = "")
    {
        var value = _values[(memory, name)];
        foreach (var field in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) value = Field(value, field);
        return value;
    }

    public void Write(int memory, string name, object value, string path = "")
    {
        if (memory == 1) throw new InvalidOperationException("Rig literal is immutable.");
        var key = (memory, name);
        if (!_types.TryGetValue(key, out var type)) throw new ArgumentException("Unknown Rig register: " + name);
        // Build and validate the replacement before changing the register.
        var next = path.Length == 0 ? value : Replace(_values[key], path.Split('.'), 0, value);
        _values[key] = Normalize(type, next);
    }

    private static bool Supported(Type type) => type == typeof(bool) || type == typeof(float) || type == typeof(double) ||
        type == typeof(string) || type == typeof(AlsDoubleVector) || type == typeof(AlsQuaternion) ||
        type == typeof(AlsPrecisePose) || type == typeof(JsonElement) ||
        type.IsValueType && typeof(IAlsRigStructValue).IsAssignableFrom(type);

    private static object Normalize(Type type, object value)
    {
        if (type == typeof(float) && value is float or double) return Convert.ToSingle(value);
        if (type == typeof(double) && value is float or double) return Convert.ToDouble(value);
        if (value is null || value.GetType() != type) throw new ArgumentException("Rig register type mismatch.");
        // Own opaque immutable data independently of the caller's document lifetime.
        return value is JsonElement element ? element.Clone() : value;
    }

    private static object Field(object value, string name) => value switch
    {
        AlsPrecisePose p when name == "Translation" => p.Position,
        AlsPrecisePose p when name == "Rotation" => p.Rotation,
        AlsPrecisePose p when name == "Scale" => p.Scale,
        AlsDoubleVector v when name == "X" => v.X,
        AlsDoubleVector v when name == "Y" => v.Y,
        AlsDoubleVector v when name == "Z" => v.Z,
        IAlsRigStructValue v => v.ReadField(name),
        _ => throw new NotSupportedException("Unknown Rig field: " + name)
    };

    private static object Replace(object old, string[] path, int index, object value)
    {
        var name = path[index];
        var next = index == path.Length - 1 ? value : Replace(Field(old, name), path, index + 1, value);
        return old switch
        {
            AlsDoubleVector v when name == "X" => v with { X = Convert.ToDouble(next) },
            AlsDoubleVector v when name == "Y" => v with { Y = Convert.ToDouble(next) },
            AlsDoubleVector v when name == "Z" => v with { Z = Convert.ToDouble(next) },
            AlsPrecisePose p when name == "Translation" => p with { Position = (AlsDoubleVector)next },
            AlsPrecisePose p when name == "Rotation" => p with { Rotation = (AlsQuaternion)next },
            AlsPrecisePose p when name == "Scale" => p with { Scale = (AlsDoubleVector)next },
            IAlsRigStructValue v => v.WithField(name, next),
            _ => throw new NotSupportedException("Unknown Rig write field: " + name)
        };
    }
}

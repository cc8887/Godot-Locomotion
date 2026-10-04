using System.Collections.ObjectModel;

namespace GodotAls.Core.Locomotion;

public sealed record AlsRigElementTransforms(AlsPrecisePose Local, AlsPrecisePose Global,
    AlsPrecisePose InitialLocal, AlsPrecisePose InitialGlobal, AlsPrecisePose? OffsetLocal,
    AlsPrecisePose? OffsetGlobal, AlsPrecisePose? InitialOffsetLocal, AlsPrecisePose? InitialOffsetGlobal);
public sealed record AlsRigElementDefinition(string Name, string? Parent, bool Control, AlsRigElementTransforms Transforms);

// Mutable hierarchy belongs to one Rig candidate. Current/initial TRS caches and
// control offsets are separate, matching URigHierarchy's single-parent paths.
public sealed class AlsRigHierarchy
{
    private sealed class Element
    {
        public required string Name;
        public required string? Parent;
        public required bool Control;
        public required AlsPrecisePose[] Local;
        public required AlsPrecisePose[] Global;
        public required AlsPrecisePose[] OffsetLocal;
        public required AlsPrecisePose[] OffsetGlobal;
        public bool[] LocalDirty = new bool[2], GlobalDirty = new bool[2],
            OffsetLocalDirty = new bool[2], OffsetGlobalDirty = new bool[2];
        public Element Clone() => new() { Name = Name, Parent = Parent, Control = Control,
            Local = (AlsPrecisePose[])Local.Clone(), Global = (AlsPrecisePose[])Global.Clone(),
            OffsetLocal = (AlsPrecisePose[])OffsetLocal.Clone(), OffsetGlobal = (AlsPrecisePose[])OffsetGlobal.Clone(),
            LocalDirty = (bool[])LocalDirty.Clone(), GlobalDirty = (bool[])GlobalDirty.Clone(),
            OffsetLocalDirty = (bool[])OffsetLocalDirty.Clone(), OffsetGlobalDirty = (bool[])OffsetGlobalDirty.Clone() };
    }
    private readonly Dictionary<string, Element> _elements;
    private readonly Dictionary<string, Element[]> _children;

    public AlsRigHierarchy(IEnumerable<AlsRigElementDefinition> definitions)
    {
        _elements = new(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
            var t = definition.Transforms;
            t.Local.Validate(); t.Global.Validate(); t.InitialLocal.Validate(); t.InitialGlobal.Validate();
            if (definition.Control)
            {
                if (t.OffsetLocal is not {} ol || t.OffsetGlobal is not {} og ||
                    t.InitialOffsetLocal is not {} iol || t.InitialOffsetGlobal is not {} iog)
                    throw new ArgumentException("Control offsets are incomplete.");
                ol.Validate(); og.Validate(); iol.Validate(); iog.Validate();
            }
            _elements.Add(definition.Name, new() { Name = definition.Name, Parent = definition.Parent, Control = definition.Control,
                Local = new[] { t.Local, t.InitialLocal }, Global = new[] { t.Global, t.InitialGlobal },
                OffsetLocal = definition.Control ? new[] { t.OffsetLocal!.Value, t.InitialOffsetLocal!.Value } : new AlsPrecisePose[2],
                OffsetGlobal = definition.Control ? new[] { t.OffsetGlobal!.Value, t.InitialOffsetGlobal!.Value } : new AlsPrecisePose[2] });
        }
        foreach (var element in _elements.Values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { element.Name };
            for (var parent = element.Parent; parent is not null; parent = _elements[parent].Parent)
                if (!_elements.ContainsKey(parent) || !seen.Add(parent))
                    throw new ArgumentException("Missing or cyclic Rig parent.");
        }
        _children = Children(_elements);
    }
    private AlsRigHierarchy(Dictionary<string, Element> elements)
    { _elements = elements; _children = Children(elements); }
    private static Dictionary<string, Element[]> Children(Dictionary<string, Element> elements) =>
        elements.Values.ToDictionary(e => e.Name, e => elements.Values.Where(c =>
            StringComparer.OrdinalIgnoreCase.Equals(c.Parent, e.Name)).ToArray(), StringComparer.OrdinalIgnoreCase);
    public AlsRigHierarchy Clone() => new(_elements.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase));
    public void CopyFrom(AlsRigHierarchy source)
    {
        if (!_elements.Keys.SequenceEqual(source._elements.Keys, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Foreign Rig hierarchy copy.");
        foreach (var (name, value) in source._elements)
        {
            var e = _elements[name];
            if (e.Parent != value.Parent || e.Control != value.Control) throw new InvalidOperationException("Foreign Rig topology.");
        }
        foreach (var (name, value) in source._elements)
        {
            var e = _elements[name];
            value.Local.CopyTo(e.Local, 0); value.Global.CopyTo(e.Global, 0);
            value.OffsetLocal.CopyTo(e.OffsetLocal, 0); value.OffsetGlobal.CopyTo(e.OffsetGlobal, 0);
            value.LocalDirty.CopyTo(e.LocalDirty, 0); value.GlobalDirty.CopyTo(e.GlobalDirty, 0);
            value.OffsetLocalDirty.CopyTo(e.OffsetLocalDirty, 0); value.OffsetGlobalDirty.CopyTo(e.OffsetGlobalDirty, 0);
        }
    }
    private Element Find(string name) => _elements.TryGetValue(name, out var e) ? e :
        throw new ArgumentException("Unknown Rig element: " + name, nameof(name));
    public AlsPrecisePose GetParent(string name, bool initial = false) => Parent(Find(name), initial ? 1 : 0);
    // OffsetTransform chooses the clean local cache when the global cache is
    // dirty. Querying Get(Global) first would change that native choice.
    public bool IsGlobalDirty(string name) => Find(name).GlobalDirty[0];
    // Full TRS, one parent at weight one. The child is a Transform control;
    // conversion through its own parent and offset preserves local storage.
    public void ApplySingleParentConstraint(string child, string parent)
    {
        if (!Find(child).Control) throw new ArgumentException("Parent constraint requires a Transform control.", nameof(child));
        var parentCurrent = Get(parent);
        var childInitial = Get(child, initial: true);
        var parentInitial = Get(parent, initial: true);
        var mixed = AlsPrecisePose.Compose(AlsPrecisePose.Relative(childInitial, parentInitial), parentCurrent).Normalized();
        mixed = mixed.Normalized();
        var local = AlsPrecisePose.Relative(mixed, GetParent(child));
        _ = Get(child, local: true);
        local = AlsPrecisePose.Relative(local, Get(child, local: true, offset: true)).Normalized();
        Set(child, local, local: true);
    }
    public AlsPrecisePose Get(string name, bool local = false, bool initial = false, bool offset = false)
    {
        var e = Find(name); int i = initial ? 1 : 0;
        if (offset && !e.Control) throw new ArgumentException("Bone has no control offset.");
        return offset ? Offset(e, local, i) : Pose(e, local, i);
    }
    private AlsPrecisePose Parent(Element e, int i) => e.Parent is null ? AlsPrecisePose.Identity : Pose(Find(e.Parent), false, i);
    private AlsPrecisePose InverseBasis(Element e, int i)
    {
        var offset = e.Control ? Offset(e, true, i) : AlsPrecisePose.Identity;
        return e.Parent is null ? offset : AlsPrecisePose.Compose(offset, Parent(e, i)).Normalized();
    }
    private AlsPrecisePose Pose(Element e, bool local, int i)
    {
        var dirty = local ? e.LocalDirty : e.GlobalDirty;
        if (!dirty[i]) return (local ? e.Local : e.Global)[i];
        if ((local ? e.GlobalDirty : e.LocalDirty)[i]) throw new InvalidOperationException("Both Rig pose caches are dirty.");
        if (local)
        {
            var parent = Parent(e, i);
            var p = AlsPrecisePose.Relative(e.Global[i], e.Control ? InverseBasis(e, i) : parent);
            if (!e.Control || e.Parent is null) p = p.Normalized();
            // URigHierarchy retains the previous local location/scale for a
            // zero result scale under a parent with at least one zero axis.
            if (ZeroAxis(p.Scale) && ZeroAxis(parent.Scale))
                p = p with { Position = e.Local[i].Position, Scale = e.Local[i].Scale };
            e.Local[i] = p;
        }
        else
        {
            var basis = e.Control ? AlsPrecisePose.Compose(Offset(e, true, i), Parent(e, i)) : Parent(e, i);
            e.Global[i] = AlsPrecisePose.Compose(e.Local[i], basis).Normalized();
        }
        dirty[i] = false;
        return (local ? e.Local : e.Global)[i];
    }
    private static bool ZeroAxis(AlsDoubleVector v) => System.Math.Abs(v.X) <= 1e-8 || System.Math.Abs(v.Y) <= 1e-8 || System.Math.Abs(v.Z) <= 1e-8;
    private AlsPrecisePose Offset(Element e, bool local, int i)
    {
        var dirty = local ? e.OffsetLocalDirty : e.OffsetGlobalDirty;
        if (!dirty[i]) return (local ? e.OffsetLocal : e.OffsetGlobal)[i];
        if ((local ? e.OffsetGlobalDirty : e.OffsetLocalDirty)[i]) throw new InvalidOperationException("Both Rig offset caches are dirty.");
        if (local)
        {
            var p = AlsPrecisePose.Relative(e.OffsetGlobal[i],
                AlsPrecisePose.Compose(AlsPrecisePose.Identity, Parent(e, i)).Normalized());
            e.OffsetLocal[i] = e.Parent is null ? p.Normalized() : p;
        }
        else e.OffsetGlobal[i] = AlsPrecisePose.Compose(e.OffsetLocal[i], Parent(e, i)).Normalized();
        dirty[i] = false;
        return (local ? e.OffsetLocal : e.OffsetGlobal)[i];
    }
    public void Set(string name, AlsPrecisePose value, bool local = false, bool initial = false,
        bool offset = false, bool children = true, bool force = false)
    {
        value.Validate(); var e = Find(name); int i = initial ? 1 : 0;
        if (offset && !e.Control) throw new ArgumentException("Bone has no control offset.");
        if (!offset && e.Control && !local)
        {
            // Native SetTransform(Global control) converts and delegates to
            // SetTransform(Local), explicitly passing force=false.
            var converted = AlsPrecisePose.Relative(value, InverseBasis(e, i));
            if (e.Parent is null) converted = converted.Normalized();
            // Settings.ApplyLimits always round-trips through FRigControlValue,
            // even with all limits disabled. This Rig's Transform controls use
            // FTransform_Float followed by a double quaternion normalization.
            converted = new(new((float)converted.Position.X, (float)converted.Position.Y, (float)converted.Position.Z),
                new AlsQuaternion((float)converted.Rotation.X, (float)converted.Rotation.Y,
                    (float)converted.Rotation.Z, (float)converted.Rotation.W).Normalized(),
                new((float)converted.Scale.X, (float)converted.Scale.Y, (float)converted.Scale.Z));
            Set(name, converted, true, initial, false, children, false);
            return;
        }
        var dirty = offset ? (local ? e.OffsetLocalDirty : e.OffsetGlobalDirty) : (local ? e.LocalDirty : e.GlobalDirty);
        var values = offset ? (local ? e.OffsetLocal : e.OffsetGlobal) : (local ? e.Local : e.Global);
        if (!dirty[i] && !force && Equal(values[i], value)) return;
        _ = offset ? Offset(e, local, i) : Pose(e, local, i);
        Propagate(e, i, children, true, true);
        if (offset)
        {
            _ = Pose(e, true, i); e.GlobalDirty[i] = true;
            values[i] = value; dirty[i] = false;
            (local ? e.OffsetGlobalDirty : e.OffsetLocalDirty)[i] = true;
            if (initial) Set(name, value, local, false, true, children, force);
        }
        else
        {
            values[i] = value; dirty[i] = false;
            (local ? e.GlobalDirty : e.LocalDirty)[i] = true;
        }
    }
    private void Propagate(Element e, int i, bool affect, bool compute, bool mark)
    {
        foreach (var child in _children[e.Name])
        {
            var dirty = affect ? child.GlobalDirty : child.LocalDirty;
            if (dirty[i]) continue;
            if (compute)
            {
                if (child.Control) _ = Offset(child, true, i);
                _ = Pose(child, affect, i);
                if (affect) Propagate(child, i, true, true, false);
            }
            if (mark)
            {
                dirty[i] = true;
                if (child.Control) child.OffsetGlobalDirty[i] = true;
                if (affect) Propagate(child, i, true, false, true);
            }
        }
    }
    public void Reset()
    {
        foreach (var e in _elements.Values)
        {
            e.Local[0] = e.Local[1]; e.Global[0] = e.Global[1];
            e.LocalDirty[0] = e.LocalDirty[1]; e.GlobalDirty[0] = e.GlobalDirty[1];
            e.OffsetLocal[0] = e.OffsetLocal[1]; e.OffsetGlobal[0] = e.OffsetGlobal[1];
            e.OffsetLocalDirty[0] = e.OffsetLocalDirty[1]; e.OffsetGlobalDirty[0] = e.OffsetGlobalDirty[1];
        }
    }
    // Native construction saves GetPose(CurrentLocal/Global), materializes
    // InitialLocal/Global before ResetPoseToInitial, then restores CurrentLocal.
    // Reading both caches in element order also resolves control offset spaces
    // before their parents move in the following Forward Solve.
    public IReadOnlyDictionary<string, AlsPrecisePose> CaptureConstructionPose(bool initial)
    {
        var pose = new Dictionary<string, AlsPrecisePose>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _elements.Values)
        {
            pose.Add(e.Name, Pose(e, true, initial ? 1 : 0));
            _ = Pose(e, false, initial ? 1 : 0);
        }
        return pose;
    }
    public void RestoreConstructionPose(IReadOnlyDictionary<string, AlsPrecisePose> pose)
    {
        if (!pose.Keys.SequenceEqual(_elements.Keys, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Foreign construction pose.");
        foreach (var (name, local) in pose) Set(name, local, local: true);
    }
    // Optimized PoseAdapter imports local storage directly, bypassing Set's
    // tolerance, normalization and child propagation. This fixed Rig's controls
    // form a separate tree; all unmapped bones are reset before the import.
    public void ImportAdapterLocalPose(IReadOnlyList<string?> mapping, ReadOnlySpan<AlsPrecisePose> pose)
    {
        if (mapping.Count != pose.Length) throw new ArgumentException("Incomplete Rig pose mapping.");
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mapping.Count; i++)
        {
            pose[i].Validate();
            if (mapping[i] is not { } name) continue;
            if (Find(name).Control || !mapped.Add(name)) throw new ArgumentException("Invalid Rig bone mapping.");
        }
        foreach (var e in _elements.Values.Where(e => e.Control))
            for (var parent = e.Parent; parent is not null; parent = Find(parent).Parent)
                if (mapped.Contains(parent)) throw new NotSupportedException("Changed Rig control dependents.");
        foreach (var e in _elements.Values.Where(e => !e.Control && !mapped.Contains(e.Name)))
        {
            e.Local[0] = Pose(e, true, 1); e.LocalDirty[0] = false; e.GlobalDirty[0] = true;
        }
        for (int i = 0; i < mapping.Count; i++)
        {
            if (mapping[i] is not { } name) continue;
            var e = Find(name); e.Local[0] = pose[i]; e.LocalDirty[0] = false; e.GlobalDirty[0] = true;
        }
    }
    public IReadOnlyDictionary<string, AlsRigElementTransforms> Snapshot()
    {
        var result = new Dictionary<string, AlsRigElementTransforms>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _elements.Values)
            result.Add(e.Name, new(Pose(e, true, 0), Pose(e, false, 0), Pose(e, true, 1), Pose(e, false, 1),
                e.Control ? Offset(e, true, 0) : null, e.Control ? Offset(e, false, 0) : null,
                e.Control ? Offset(e, true, 1) : null, e.Control ? Offset(e, false, 1) : null));
        return new ReadOnlyDictionary<string, AlsRigElementTransforms>(result);
    }
    private static bool Equal(AlsPrecisePose a, AlsPrecisePose b)
    {
        const double tolerance = .0001f;
        bool Near(AlsQuaternion x, AlsQuaternion y) => System.Math.Abs(x.X - y.X) <= tolerance &&
            System.Math.Abs(x.Y - y.Y) <= tolerance && System.Math.Abs(x.Z - y.Z) <= tolerance && System.Math.Abs(x.W - y.W) <= tolerance;
        return (a.Position - b.Position).NearlyZero(tolerance) && (a.Scale - b.Scale).NearlyZero(tolerance) &&
            (Near(a.Rotation, b.Rotation) || Near(a.Rotation, b.Rotation * -1));
    }
}

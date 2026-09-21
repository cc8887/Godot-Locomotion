using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsPrimitiveGeometry(int Body, int Shape, string Type, AlsDoubleVector Center,
    float Radius, AlsCapsuleGeometry? Capsule, AlsDoubleVector BoxHalf, AlsPrecisePose ProxyLocal);

public static class AlsPrimitiveGeometryCompiler
{
    public const string Observation = "Actual unwrapped sphere center/radius, capsule endpoints/axis/height/radius and box bounds in leaf-local space; no inferred authored dimensions or simulation tick";
    public static AlsPrimitiveGeometry[] Compile(string json, string runtimeJson, string meshPath)
    {
        try
        {
            var root = JsonNode.Parse(json)!.AsObject(); var stripped = root.DeepClone().AsObject();
            Require(root["primitiveObservation"]!.GetValue<string>() == Observation, "Primitive observation differs.");
            stripped.Remove("primitiveObservation");
            foreach (var rig in stripped["meshes"]!.AsArray()) foreach (var body in rig!["bodies"]!.AsArray())
                foreach (var shape in body!["runtimeShapes"]!.AsArray()) shape!.AsObject().Remove("primitiveGeometry");
            Require(JsonNode.DeepEquals(stripped, JsonNode.Parse(runtimeJson)), "Primitive observation is bound to different runtime geometry.");
            var mesh = root["meshes"]!.AsArray().Single(m => m!["mesh"]!.GetValue<string>() == meshPath)!;
            var result = new List<AlsPrimitiveGeometry>();
            foreach (var body in mesh["bodies"]!.AsArray()) foreach (var shape in body!["runtimeShapes"]!.AsArray())
            {
                var type = shape!["type"]!.GetValue<string>(); if (type == "Convex") { Require(shape["primitiveGeometry"] is null, "Unexpected convex primitive."); continue; }
                Require(shape["wrapper"]!.GetValue<string>() == "plain", "Wrapped primitive unsupported.");
                var p = shape["primitiveGeometry"]!; var center = AlsDoubleVector.Zero; var radius = 0f;
                var half = AlsDoubleVector.Zero; AlsCapsuleGeometry? capsule = null; var rotation = AlsQuaternion.Identity;
                AlsDoubleVector min, max;
                if (type == "Box")
                {
                    min = V(p["min"]!); max = V(p["max"]!); center = (min + max) * .5; half = (max - min) * .5;
                    Require(center == AlsDoubleVector.Zero && half.X > 0 && half.Y > 0 && half.Z > 0, "Native box must be centered and nondegenerate.");
                }
                else
                {
                    Require(type is "Sphere" or "Capsule", "Unknown native primitive."); center = V(p["center"]!); radius = (float)N(p["radius"]!);
                    Require(float.IsFinite(radius) && radius > 0 && radius == (float)N(shape["marginCm"]!), "Invalid primitive radius.");
                    if (type == "Capsule")
                    {
                        var start = V(p["endpoint0"]!); var end = V(p["endpoint1"]!); var axis = V(p["axis"]!);
                        var height = (float)N(p["height"]!); capsule = new(start.ToSingle(), axis.ToSingle(), height, radius); capsule.Value.Validate();
                        Require(new AlsDoubleVector(start.ToSingle()) == start && new AlsDoubleVector(axis.ToSingle()) == axis,
                            "Capsule must retain native float storage.");
                        Require((new AlsDoubleVector(start.ToSingle() + axis.ToSingle() * height) - end).NearlyZero(1e-5) &&
                            (new AlsDoubleVector(start.ToSingle() + axis.ToSingle() * (.5f * height)) - center).NearlyZero(1e-5), "Capsule segment fields disagree.");
                        // Proxy needs a centered Z capsule. Core keeps original
                        // endpoint/axis and never reconstructs them from this rotation.
                        var unit = axis.RemoveScaling();
                        if (unit.Z < -1 + 1e-12) rotation = new(1, 0, 0, 0);
                        else
                        {
                            var q = new AlsQuaternion(-unit.Y, unit.X, 0, 1 + unit.Z);
                            rotation = q * (1 / Math.Sqrt(q.LengthSquared));
                        }
                        min = new(Math.Min(start.X, end.X) - radius, Math.Min(start.Y, end.Y) - radius, Math.Min(start.Z, end.Z) - radius);
                        max = new(Math.Max(start.X, end.X) + radius, Math.Max(start.Y, end.Y) + radius, Math.Max(start.Z, end.Z) + radius);
                    }
                    else { var extent = new AlsDoubleVector(radius, radius, radius); min = center - extent; max = center + extent; }
                }
                Require((min - V(shape["boundsMinCm"]!)).NearlyZero(1e-4) && (max - V(shape["boundsMaxCm"]!)).NearlyZero(1e-4), "Primitive bounds disagree with native leaf.");
                result.Add(new(body["index"]!.GetValue<int>(), shape["authoredIndex"]!.GetValue<int>(), type, center, radius, capsule, half,
                    new(center, rotation, AlsDoubleVector.One)));
            }
            return result.OrderBy(p => p.Body).ThenBy(p => p.Shape).ToArray();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or ArgumentException or InvalidOperationException or NullReferenceException or FormatException or OverflowException)
        { throw new InvalidDataException("Invalid native primitive geometry.", e); }
    }
    private static double N(JsonNode n) { var v = n.GetValue<double>(); Require(double.IsFinite(v), "Nonfinite primitive field."); return v; }
    private static AlsDoubleVector V(JsonNode n) { var a = n.AsArray(); Require(a.Count == 3, "Invalid primitive vector."); return new(N(a[0]!), N(a[1]!), N(a[2]!)); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}

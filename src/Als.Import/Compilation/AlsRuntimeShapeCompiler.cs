using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRuntimeShape(int Body, int Shape, int NativeIndex, string Type,
    string Wrapper, float MarginCm, float? InnerMarginCm, AlsDoubleVector Scale,
    AlsPrecisePose LeafLocal, AlsDoubleVector BoundsMinCm, AlsDoubleVector BoundsMaxCm);

// Runtime geometry is not the authored FKShapeElem transform. In particular,
// cooked convex vertices may already include that transform.
public static class AlsRuntimeShapeCompiler
{
    public const string Observation = "External game-thread particle shapes after physics creation; leaf wrapper and margin observed, authored index matched by shape user-data identity; no simulation tick";
    public static AlsRuntimeShape[] Compile(string json, string authoredJson, string meshPath)
    {
        try { return Read(json, authoredJson, meshPath); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or
            ArgumentException or FormatException or OverflowException or NullReferenceException)
        { throw new InvalidDataException("Invalid runtime physics shapes.", e); }
    }
    private static AlsRuntimeShape[] Read(string json, string authoredJson, string meshPath)
    {
        var root=JsonNode.Parse(json)!.AsObject();
        Require(root["shapeObservation"]!.GetValue<string>()==Observation,"Runtime observation boundary differs.");
        var authored=JsonNode.Parse(authoredJson)!;
        var stripped=root.DeepClone().AsObject();stripped.Remove("shapeObservation");
        foreach(var rig in stripped["meshes"]!.AsArray())
            foreach(var body in rig!["bodies"]!.AsArray())
                Require(body!.AsObject().Remove("runtimeShapes"),"Missing runtime shape set.");
        Require(JsonNode.DeepEquals(stripped,authored),"Runtime observation is bound to different authored physics data.");
        var definition=AlsPhysicsAssetCompiler.Compile(authoredJson,meshPath);
        var mesh=root["meshes"]!.AsArray().Single(m=>m!["mesh"]!.GetValue<string>()==meshPath)!;
        var result=new List<AlsRuntimeShape>();
        foreach(var body in mesh["bodies"]!.AsArray())
        {
            var index=body!["index"]!.GetValue<int>();var source=definition.Bodies[index];
            var rows=body["runtimeShapes"]!.AsArray();
            Require(rows.Count==source.Shapes.Length,"Runtime shape count differs.");
            var seen=new HashSet<int>();var nativeSeen=new HashSet<int>();
            foreach(var row in rows)
            {
                var shape=row!["authoredIndex"]!.GetValue<int>();var native=row["nativeIndex"]!.GetValue<int>();
                Require((uint)shape<source.Shapes.Length&&(uint)native<rows.Count&&seen.Add(shape)&&nativeSeen.Add(native),"Invalid runtime shape identity.");
                var type=row["type"]!.GetValue<string>();var wrapper=row["wrapper"]!.GetValue<string>();
                Require(type.ToLowerInvariant()==source.Shapes[shape].Type,"Runtime geometry type differs.");
                Require(wrapper is "plain" or "scaled" or "instanced","Unsupported runtime wrapper.");
                Require(wrapper=="plain"||type=="Convex","Unsupported wrapped geometry.");
                var code=type switch {"Sphere"=>0,"Box"=>1,"Capsule"=>3,"Convex"=>8,_=>-1};
                Require(code>=0&&row["typeCode"]!.GetValue<int>()==(code|(wrapper=="scaled"?128:wrapper=="instanced"?64:0)),"Runtime type flags differ.");
                var scale=V(row["scale"]!);Require(scale.X!=0&&scale.Y!=0&&scale.Z!=0,"Singular runtime scale.");
                Require(wrapper=="scaled"||scale==AlsDoubleVector.One,"Unscaled wrapper has scale.");
                var margin=N(row["marginCm"]!);Require(margin>=0&&margin<=float.MaxValue,"Invalid runtime margin.");
                float? inner=null;
                if(wrapper!="plain")
                {
                    Require(row["innerType"]!.GetValue<string>()=="Convex","Invalid convex inner type.");
                    var n=N(row["innerMarginCm"]!);Require(n>=0&&n<=float.MaxValue,"Invalid inner margin.");inner=(float)n;
                }
                var local=row["leafLocal"]!;var q=local["rotation"]!.AsArray();Require(q.Count==4,"Invalid leaf rotation.");
                var pose=new AlsPrecisePose(V(local["translation"]!),new(N(q[0]!),N(q[1]!),N(q[2]!),N(q[3]!)),V(local["scale"]!));
                pose.Validate(.001);Require(pose.Scale==AlsDoubleVector.One,"Leaf transform must be rigid.");
                var min=V(row["boundsMinCm"]!);var max=V(row["boundsMaxCm"]!);
                Require(min.X<max.X&&min.Y<max.Y&&min.Z<max.Z,"Invalid runtime bounds.");
                result.Add(new(index,shape,native,type,wrapper,(float)margin,inner,scale,pose,min,max));
            }
        }
        return result.OrderBy(s=>s.Body).ThenBy(s=>s.Shape).ToArray();
    }
    private static double N(JsonNode node)
    {var value=node.GetValue<double>();Require(double.IsFinite(value),"Nonfinite runtime geometry.");return value;}
    private static AlsDoubleVector V(JsonNode node)
    {var a=node.AsArray();Require(a.Count==3,"Invalid runtime vector.");return new(N(a[0]!),N(a[1]!),N(a[2]!));}
    private static void Require(bool condition,string message)
    {if(!condition)throw new InvalidDataException(message);}
}

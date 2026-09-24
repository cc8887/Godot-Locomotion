using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredTriangulationProfile
{
    private readonly string[] _samples;
    public AlsTriangulatedBlendSpace Weights { get; }
    public ReadOnlySpan<string> Samples=>_samples;
    public Vector2 FilterWindows { get; }
    public bool Loop { get; }
    internal AlsRefactoredTriangulationProfile(AlsTriangulatedBlendSpace weights,string[] samples,bool lean)
    {Weights=weights;_samples=samples;FilterWindows=lean?Vector2.Zero:new(.2f,.4f);Loop=!lean;}
}

public static class AlsRefactoredTriangulationCompiler
{
    public static IReadOnlyDictionary<string,AlsRefactoredTriangulationProfile> Compile(string inputsJson,string catalogJson,Func<string,byte[]> read)
    {
        using var doc=JsonDocument.Parse(inputsJson);var root=doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32()==1&&root.GetProperty("catalogSha256").GetString()!.Equals(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalogJson))),StringComparison.OrdinalIgnoreCase),"Foreign triangle source catalog.");
        var catalog=new AlsRefactoredAnimationCatalog(catalogJson,read);
        var result=new Dictionary<string,AlsRefactoredTriangulationProfile>(StringComparer.Ordinal);
        foreach(var row in root.GetProperty("spaces").EnumerateArray())
        {
            var path=row.GetProperty("source").GetString()!;var payload=catalog.Read(path);
            Require(payload.GetProperty("class").GetString()=="BlendSpace","Expected 2D original BlendSpace.");
            var lean=path.Contains("/Lean/",StringComparison.Ordinal);
            Require(lean||path.Contains("/WalkRun/BS_Als_WalkRun_",StringComparison.Ordinal),"Unsupported 2D asset.");
            ValidatePolicy(payload.GetProperty("nativeText").GetString()!,lean);
            var samples=payload.GetProperty("samples").EnumerateArray().ToArray();
            Require(samples.Length==(lean?5:4),"Unexpected original sample layout.");
            var names=samples.Select(s=>s.GetProperty("sequence").GetString()!).ToArray();
            Require(names.Distinct(StringComparer.Ordinal).Count()==names.Length&&names.All(n=>catalog.Assets[n].Class=="AnimSequence"),"Unbound or duplicate sample animations.");
            for(var i=0;i<samples.Length;i++)
            {
                var point=samples[i].GetProperty("point").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                Require(point.Length==3&&point.All(double.IsFinite),"Invalid sample coordinates.");
                var location=point.All(v=>v==0)?"":",SampleValue=(X="+point[0].ToString("F6",CultureInfo.InvariantCulture)+
                    ",Y="+point[1].ToString("F6",CultureInfo.InvariantCulture)+",Z="+point[2].ToString("F6",CultureInfo.InvariantCulture)+")";
                Require(payload.GetProperty("nativeText").GetString()!.Contains($"   SampleData({i})=(Animation=\"/Script/Engine.AnimSequence'{names[i]}'\"{location})",StringComparison.Ordinal),
                    "Structured sample differs from authored binding.");
            }
            var data=row.GetProperty("data");Require(data.GetProperty("segments").GetArrayLength()==0,"Mixed triangle and segment topology.");
            var triangles=data.GetProperty("triangles").EnumerateArray().ToArray();
            Require(triangles.Length==(lean?4:2),"Incomplete original triangulation.");
            var vertices=new List<AlsBlendTriangleVertex>();
            foreach(var t in triangles)
            {
                Require(t.GetProperty("sampleIndices").GetArrayLength()==3&&t.GetProperty("vertices").GetArrayLength()==3&&
                    t.GetProperty("edgeInfo").GetArrayLength()==3,"Invalid triangle dimensions.");
                for(var i=0;i<3;i++)
                {
                    var sample=t.GetProperty("sampleIndices")[i].GetInt32();Require((uint)sample<(uint)samples.Length,"Invalid sample index.");
                    var point=Point(t.GetProperty("vertices")[i]);var source=samples[sample].GetProperty("point");
                    Require(point==new AlsBlendPoint((source[0].GetDouble()+(lean?1:0))/(lean?2:1),
                        (source[1].GetDouble()+(lean?1:0))/(lean?2:1))&&source[2].GetDouble()==0,"Triangle vertex differs from its bound sample.");
                    var edge=t.GetProperty("edgeInfo")[i];
                    vertices.Add(new(sample,point,Point(edge.GetProperty("normal")),edge.GetProperty("neighbourTriangleIndex").GetInt32(),
                        edge.GetProperty("adjacentPerimeterTriangleIndices")[0].GetInt32(),edge.GetProperty("adjacentPerimeterTriangleIndices")[1].GetInt32(),
                        edge.GetProperty("adjacentPerimeterVertexIndices")[0].GetInt32(),edge.GetProperty("adjacentPerimeterVertexIndices")[1].GetInt32()));
                }
            }
            ValidateEdges(vertices);
            Require(result.TryAdd(path,new(new(vertices.ToArray(),names.Length,new(lean?-1:0,lean?-1:0),new(1,1)),names,lean)),"Duplicate triangle source.");
        }
        Require(result.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(catalog.Assets.Values.Where(a=>a.Class=="BlendSpace").Select(a=>a.Source)),"Missing original 2D BlendSpace.");
        return new ReadOnlyDictionary<string,AlsRefactoredTriangulationProfile>(result);
    }
    private static AlsBlendPoint Point(JsonElement p)=>new(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble());
    private static void ValidateEdges(List<AlsBlendTriangleVertex> v)
    {
        for(var index=0;index<v.Count;index++)
        {
            var a=v[index];var b=v[index/3*3+(index%3+1)%3];var d=b.Point-a.Point;var length=Math.Sqrt(d.Dot(d));
            Require(length>0&&Math.Abs(a.Normal.X-d.Y/length)<1e-12&&Math.Abs(a.Normal.Y+d.X/length)<1e-12,"Triangle edge normal differs.");
            if(a.Neighbour>=0)
                Require(a.Neighbour<v.Count/3&&Enumerable.Range(0,3).Any(i=>v[a.Neighbour*3+i].Point==b.Point&&
                    v[a.Neighbour*3+(i+1)%3].Point==a.Point&&v[a.Neighbour*3+i].Neighbour==index/3),"Triangle adjacency differs.");
            else
            {
                void Check(int triangle,int corner,AlsBlendPoint endpoint,bool start)
                {
                    if(triangle==-1&&corner==-1)
                    {
                        var adjacent=index/3*3+(index%3+(start?2:1))%3;
                        Require(v[adjacent].Neighbour<0,"Missing perimeter link to another triangle.");
                        return;
                    }
                    Require(triangle>=0&&triangle<v.Count/3&&corner>=0&&corner<3,"Missing convex perimeter link.");
                    Require(v[triangle*3+corner].Neighbour<0&&v[triangle*3+(start?(corner+1)%3:corner)].Point==endpoint,"Disconnected perimeter link.");
                }
                Check(a.PerimeterStartTriangle,a.PerimeterStartVertex,a.Point,true);
                Check(a.PerimeterEndTriangle,a.PerimeterEndVertex,b.Point,false);
            }
        }
    }
    private static void ValidatePolicy(string native,bool lean)
    {
        var properties=Regex.Matches(native.Replace("\r",""),@"(?m)^   (\w+(?:\(\d+\))?)=(.*)$")
            .ToDictionary(m=>m.Groups[1].Value,m=>m.Groups[2].Value,StringComparer.Ordinal);
        string[] allowed=["bLoop","PreviewBasePose","AnimLength","SampleIndexWithMarkers","BlendSpaceData","Skeleton","ThumbnailInfo","PreviewSkeletalMesh",
            "BlendParameters(0)","BlendParameters(1)","InterpolationParam(0)","InterpolationParam(1)","SampleData(0)","SampleData(1)","SampleData(2)","SampleData(3)","SampleData(4)"];
        Require(properties.Keys.All(allowed.Contains),"Unsupported nondefault 2D blend policy.");
        Require(properties.GetValueOrDefault("bLoop")== (lean?"False":null),"Loop policy differs.");
        for(var i=0;i<2;i++)
        {
            Require(properties.GetValueOrDefault($"InterpolationParam({i})")== (lean?null:$"(InterpolationTime={(i==0?"0.200000":"0.400000")},InterpolationType=BSIT_Cubic)"),"Filter policy differs.");
            var axis=properties[$"BlendParameters({i})"];
            var expected=lean?$"(DisplayName=\"Lean {(i==0?"Right":"Forward")} Amount\",Min=-1.000000,Max=1.000000":
                $"(DisplayName=\"{(i==0?"Stride":"Walk Run")} Amount\",Max=1.000000,GridNum=1";
            Require(axis==expected+")"||lean&&axis==expected+",GridNum=2)","Axis range/wrapping differs.");
        }
        foreach(var p in properties.Where(p=>p.Key.StartsWith("SampleData(",StringComparison.Ordinal)))
            Require(Regex.IsMatch(p.Value,@"^\(Animation=""/Script/Engine.AnimSequence'[^']+'""(?:,SampleValue=\(X=[-0-9.]+,Y=[-0-9.]+,Z=0\.000000\))?\)$"),"Unsupported sample rate/frame/mirror policy.");
    }
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}

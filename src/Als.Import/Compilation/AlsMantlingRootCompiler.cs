using System.Collections.ObjectModel;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Builds absolute root samplers from original root channels, never native sampled references.
/// This root-only projection is not a full character pose resource.</summary>
public static class AlsMantlingRootCompiler
{
    public static IReadOnlyDictionary<string, AlsMontageRootTransformSampler> Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            Text(root,"source") == "AnimDataModel.BoneAnimationTracks.InternalTrackData", "Unsupported mantle root source.");
        var sources = new Dictionary<string, (AlsRawRootMotionSampler Sampler, float Rate)>(StringComparer.Ordinal);
        foreach (var row in root.GetProperty("sequences").EnumerateArray())
        {
            var path = Text(row,"source");
            Require(!string.IsNullOrWhiteSpace(path) && !sources.ContainsKey(path), "Duplicate or empty source identity.");
            var count = row.GetProperty("sampledKeyCount").GetInt32();
            Require(count > 0, "Invalid source key count.");
            var tracks = row.GetProperty("tracks");
            Require(tracks.GetArrayLength() <= 1, "Root projection must contain only its original root track.");
            var present = tracks.GetArrayLength() == 1;
            var reference = Pose(row.GetProperty("rootReference"));
            var keys = new AlsLocalPose[count];
            var absoluteKeyCount = count;
            if (present)
            {
                var track = tracks[0];
                Require(Text(track,"bone").Equals(Text(row,"rootName"),StringComparison.OrdinalIgnoreCase), "Unexpected root bone.");
                var positions = Channel(track,"positions",count,3,false);
                var rotations = Channel(track,"rotations",count,4,false);
                var scales = Channel(track,"scales",count,3,true);
                absoluteKeyCount = System.Math.Min(positions.Length,System.Math.Min(rotations.Length,scales.Length));
                for (var i = 0; i < count; i++)
                {
                    var p = positions[positions.Length == 1 ? 0 : i];
                    var q = rotations[rotations.Length == 1 ? 0 : i];
                    var s = scales.Length == 0 ? Vector3.One : Vector(scales[scales.Length == 1 ? 0 : i]);
                    keys[i] = new(Vector(p),new(q[0],q[1],q[2],q[3]),s);
                }
            }
            var interpolation = Text(row,"interpolation") switch
            {
                "Linear" => AlsRawAnimationInterpolation.Linear,
                "Step" => AlsRawAnimationInterpolation.Step,
                _ => throw new ArgumentException("Unsupported source interpolation.")
            };
            var identity = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
            var data = new AlsRawAnimationPoseData(new(sources.Count,identity,path,0),
                row.GetProperty("frameRateNumerator").GetInt32(), row.GetProperty("frameRateDenominator").GetInt32(),
                count,row.GetProperty("playLength").GetDouble(),interpolation,[0],[],[present],keys,[]);
            var rate = Number(row,"rateScale");
            sources.Add(path,(new(data,reference,false,absoluteKeyCount),rate));
        }
        var result = new Dictionary<string, AlsMontageRootTransformSampler>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var montage in root.GetProperty("montages").EnumerateArray())
        {
            var segments = new List<AlsMontageRootSegment>();
            foreach (var row in montage.GetProperty("segments").EnumerateArray())
            {
                var path = Text(row,"sequence");
                Require(sources.TryGetValue(path,out var source), "Unbound mantle source sequence.");
                used.Add(path);
                segments.Add(new(Number(row,"trackStart"),Number(row,"animationStart"),Number(row,"animationEnd"),
                    Number(row,"segmentRate"),source.Rate,row.GetProperty("loopCount").GetInt32(),source.Sampler));
            }
            var montagePath = Text(montage,"path");
            Require(!string.IsNullOrWhiteSpace(montagePath) && result.TryAdd(montagePath,new(segments.ToArray())),
                "Duplicate or empty mantle montage identity.");
        }
        Require(result.Count > 0 && used.SetEquals(sources.Keys), "Empty or unrelated mantle source closure.");
        return new ReadOnlyDictionary<string, AlsMontageRootTransformSampler>(result);
    }

    private static float[][] Channel(JsonElement track,string field,int count,int size,bool empty)
    {
        var values = track.GetProperty(field).EnumerateArray().Select(v => v.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();
        Require(values.Length == 1 || values.Length == count || empty && values.Length == 0, "Invalid original root channel count.");
        Require(values.All(v => v.Length == size && v.All(float.IsFinite)), "Invalid original root components.");
        return values;
    }
    private static AlsPrecisePose Pose(JsonElement row)
    {
        double[] Values(string field,int count)
        {
            var values = row.GetProperty(field).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            Require(values.Length == count && values.All(double.IsFinite),"Invalid reference root components.");
            return values;
        }
        var p=Values("position",3);var q=Values("rotation",4);var s=Values("scale",3);
        var pose=new AlsPrecisePose(new(p[0],p[1],p[2]),new(q[0],q[1],q[2],q[3]),new(s[0],s[1],s[2]));
        pose.Validate();return pose;
    }
    private static Vector3 Vector(float[] v)=>new(v[0],v[1],v[2]);
    private static string Text(JsonElement row,string key)=>row.GetProperty(key).GetString() ?? throw new ArgumentException(key);
    private static float Number(JsonElement row,string key)
    { var value=row.GetProperty(key).GetSingle();Require(float.IsFinite(value),"Nonfinite mantle value.");return value; }
    private static void Require(bool condition,string message) { if(!condition) throw new ArgumentException(message); }
}

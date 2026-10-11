using System.Text;
using System.Text.RegularExpressions;
using GodotAls.Core.Diagnostics;
using GodotAls.Import.Resources;

namespace GodotAls.Import.Compilation;

internal static class AlsNativeNestedGraph
{
    internal static string Extract(string text, string source, string path)
    {
        if (!path.StartsWith(source + ":", StringComparison.Ordinal))
            throw new ArgumentException("Foreign nested graph path.");
        return ImportResourceScope.Current is { } scope
            ? scope.ExtractGraph(text, source, path) : new Index(text, source).Extract(path);
    }

    // T3D has separate declaration and definition trees, with repeated short
    // names. Index complete outer paths once; validate/dedent only requested slices.
    internal sealed class Index
    {
        private sealed record Range(string Path, string Root, int Start, int End, int Indent, bool Declaration);
        private static readonly Regex BeginObject = new("^( *)Begin Object (?:Class=[^ ]+ )?Name=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        private readonly string[] _lines;
        private readonly string _source;
        private readonly Dictionary<string, List<Range>> _ranges = new(StringComparer.Ordinal);

        internal Index(string text, string source)
        {
            using var trace = AlsPerformanceTrace.Begin("native_graph.index", source);
            _source = source;
            _lines = text.Replace("\r", "").Split('\n');
            var stack = new Stack<Range>();
            for (var i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i];
                var begin = BeginObject.Match(line);
                if (begin.Success)
                {
                    var name = begin.Groups[2].Value;
                    var parent = stack.TryPeek(out var outer) ? outer : null;
                    var path = parent is null ? source + ":" : parent.Path + (stack.Count == 1 ? "" : ".") + name;
                    stack.Push(new(path, parent?.Root ?? name, i, -1, begin.Groups[1].Length,
                        line.Contains("Begin Object Class=", StringComparison.Ordinal)));
                }
                else if (line.Trim() == "End Object")
                {
                    if (!stack.TryPop(out var range)) throw new ArgumentException("Unbalanced native object tree.");
                    if (!_ranges.TryGetValue(range.Path, out var ranges)) _ranges.Add(range.Path, ranges = []);
                    ranges.Add(range with { End = i });
                }
            }
            if (stack.Count != 0) throw new ArgumentException("Unbalanced native object tree.");
            trace?.Complete(_lines.Length);
        }

        internal string Extract(string path)
        {
            using var trace = AlsPerformanceTrace.Begin("native_graph.extract", path);
            if (!_ranges.TryGetValue(path, out var ranges) || ranges.Count(r => r.Declaration) != 1 || ranges.Count(r => !r.Declaration) != 1)
                throw new ArgumentException("Missing or ambiguous native graph declaration/definition.");
            var root = _source[(_source.LastIndexOf('.') + 1)..];
            var result = new StringBuilder();
            long count = 0;
            foreach (var range in ranges.OrderBy(r => r.Start))
            {
                if (range.Root != root) throw new ArgumentException("Foreign native Blueprint root.");
                for (var j = range.Start; j <= range.End; j++)
                {
                    var line = _lines[j];
                    if (line.Length < range.Indent || line.AsSpan(0, range.Indent).ContainsAnyExcept(' '))
                        throw new ArgumentException("Invalid native graph indentation.");
                    result.AppendLine(line[range.Indent..]);
                    count++;
                }
            }
            var extracted = result.ToString();
            trace?.Complete(count);
            return extracted;
        }
    }
}

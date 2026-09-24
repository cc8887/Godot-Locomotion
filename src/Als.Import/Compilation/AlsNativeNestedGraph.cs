using System.Text;
using System.Text.RegularExpressions;

namespace GodotAls.Import.Compilation;

internal static class AlsNativeNestedGraph
{
    // T3D has separate declaration and definition trees, with repeated short names.
    // Resolve the complete outer chain before dedenting for the direct-node parser.
    internal static string Extract(string text, string source, string path)
    {
        if (!path.StartsWith(source + ":", StringComparison.Ordinal))
            throw new ArgumentException("Foreign nested graph path.");
        var lines = text.Replace("\r", "").Split('\n');
        var stack = new List<(string Name, int Start, int Indent, bool Declaration)>();
        var result = new StringBuilder(); var declarations = 0; var definitions = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var begin = Regex.Match(line, "^( *)Begin Object (?:Class=[^ ]+ )?Name=\"([^\"]+)\"");
            if (begin.Success)
                stack.Add((begin.Groups[2].Value, i, begin.Groups[1].Length, line.Contains("Begin Object Class=", StringComparison.Ordinal)));
            else if (line.Trim() == "End Object")
            {
                if (stack.Count == 0) throw new ArgumentException("Unbalanced native object tree.");
                var item = stack[^1];
                if (source + ":" + string.Join(".", stack.Skip(1).Select(s => s.Name)) == path)
                {
                    if (stack[0].Name != source[(source.LastIndexOf('.') + 1)..])
                        throw new ArgumentException("Foreign native Blueprint root.");
                    if (item.Declaration) declarations++; else definitions++;
                    for (var j = item.Start; j <= i; j++)
                    {
                        if (lines[j].Length < item.Indent || lines[j][..item.Indent].Any(c => c != ' '))
                            throw new ArgumentException("Invalid native graph indentation.");
                        result.AppendLine(lines[j][item.Indent..]);
                    }
                }
                stack.RemoveAt(stack.Count - 1);
            }
        }
        if (stack.Count != 0 || declarations != 1 || definitions != 1)
            throw new ArgumentException("Missing or ambiguous native graph declaration/definition.");
        return result.ToString();
    }
}

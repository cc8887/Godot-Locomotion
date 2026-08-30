using System.Text.Json;
using Json.Schema;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Als.TraceSchemaValidator <schema> <document> [document ...]");
    return 2;
}

try
{
    var schema = JsonSchema.FromText(File.ReadAllText(args[0]));
    foreach (var path in args.Skip(1))
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = schema.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });
        if (!result.IsValid)
        {
            Console.Error.WriteLine($"{Path.GetFileName(path)} failed draft-2020-12 schema validation.");
            foreach (var detail in (result.Details ?? []).Where(detail => detail.Errors is { Count: > 0 }))
            {
                Console.Error.WriteLine($"{detail.InstanceLocation}: {string.Join("; ", detail.Errors!.Values)}");
            }
            return 1;
        }
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

return 0;

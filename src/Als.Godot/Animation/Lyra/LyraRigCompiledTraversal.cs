using System.Text.Json;
using GodotAls.Core.Animation;
using LyraRigOperand = GodotAls.Core.Animation.AlsRigOperand;
using LyraRigInstruction = GodotAls.Core.Animation.AlsRigInstruction;
using LyraRigBranch = GodotAls.Core.Animation.AlsRigBranch;

namespace GodotAls.Animation.Lyra;

// Fixed original FootPlant bytecode: no slices, function calls or predicates.
// Thus the native lazy hash (execution number + call stack) has one value per
// entry execution. Work-register and per-argument lazy caches remain distinct.
internal sealed class LyraRigCompiledTraversal
{
    private readonly AlsRigTraversal _core;
    public IReadOnlyList<LyraRigInstruction> Instructions => _core.Instructions;
    public IReadOnlyList<LyraRigBranch> Branches => _core.Branches;
    private static readonly HashSet<string> Supported = ["Execute", "Copy", "Zero", "JumpForward", "JumpBackward", "JumpAbsolute", "JumpToBranch", "RunInstructions", "Exit"];

    public LyraRigCompiledTraversal(JsonElement program)
    {
        var rows = program.GetProperty("instructions"); var flow = program.GetProperty("flow");
        var ops = flow.GetProperty("instructions");
        if (rows.GetArrayLength() != 436 || ops.GetArrayLength() != rows.GetArrayLength())
            throw new NotSupportedException("Changed original FootPlant instruction layout.");
        var instructions = new LyraRigInstruction[rows.GetArrayLength()];
        for (int i = 0; i < instructions.Length; i++)
        {
            var row = rows[i]; var op = ops[i]; var code = op.GetProperty("opcode").GetString()!;
            if (row.GetProperty("index").GetInt32() != i || op.GetProperty("index").GetInt32() != i || !Supported.Contains(code))
                throw new NotSupportedException("Unsupported FootPlant opcode: " + code);
            var text = row.GetProperty("text").GetString()!; int dot = text.IndexOf(". ", StringComparison.Ordinal);
            string function = code == "Execute" ? text[(dot + 2)..text.IndexOf('(')] : "";
            var operands = row.GetProperty("operands").EnumerateArray().Select(o => new LyraRigOperand(
                o.GetProperty("memory").GetInt32(), o.GetProperty("register").GetInt32(), o.GetProperty("offset").GetInt32(),
                o.GetProperty("name").GetString()!, o.GetProperty("path").GetString()!)).ToArray();
            int Field(string name) => op.TryGetProperty(name, out var value) ? value.GetInt32() : -1;
            var dispatch = function == "FRigVMFunction_ControlFlowBranch::Execute" ? AlsRigDispatch.ControlFlowBranch :
                function.StartsWith("DISPATCH_RigVMDispatch_If::", StringComparison.Ordinal) ? AlsRigDispatch.LazyIf : AlsRigDispatch.Unit;
            instructions[i] = new(i, code, function, operands, Field("first"), Field("last"), Field("distance"), Field("firstBranch"), dispatch);
        }
        var branches = flow.GetProperty("branches").EnumerateArray().Select(b => new LyraRigBranch(
            b.GetProperty("index").GetInt32(), b.GetProperty("instruction").GetInt32(), b.GetProperty("argument").GetInt32(),
            b.GetProperty("label").GetString()!, b.GetProperty("first").GetInt32(), b.GetProperty("last").GetInt32())).ToArray();
        var entries = program.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("instruction").GetInt32());
        if (entries.Count != 2 || !entries.TryGetValue("Forwards Solve", out var forwards) || forwards != 0 ||
            !entries.TryGetValue("Construction", out var construction) || construction != 401)
            throw new NotSupportedException("Changed original FootPlant entries.");
        try { _core = new(instructions, branches, entries); }
        catch (ArgumentException e) { throw new NotSupportedException("Invalid FootPlant program.", e); }
    }

    public int[] Execute(string entry, IAlsRigInstructionExecutor units) => _core.Execute(entry, units);
}

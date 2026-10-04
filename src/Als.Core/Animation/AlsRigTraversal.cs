namespace GodotAls.Core.Animation;

public readonly record struct AlsRigOperand(int Memory, int Register, int Offset, string Name, string Path);
public enum AlsRigDispatch { Unit, ControlFlowBranch, LazyIf }
public sealed record AlsRigInstruction(int Index, string Opcode, string Function,
    IReadOnlyList<AlsRigOperand> Operands, int First = -1, int Last = -1, int Distance = -1, int FirstBranch = -1,
    AlsRigDispatch Dispatch = AlsRigDispatch.Unit);
public sealed record AlsRigBranch(int Index, int Instruction, int Argument, string Label, int First, int Last);

public interface IAlsRigInstructionExecutor
{
    void Copy(AlsRigOperand source, AlsRigOperand target);
    void Zero(AlsRigOperand operand);
    bool ReadBool(AlsRigOperand operand);
    string ReadName(AlsRigOperand operand);
    void WriteName(AlsRigOperand operand, string value);
    void Execute(AlsRigInstruction instruction);
}

// Single entry executions without calls/slices. Work-state and lazy-argument
// caches are distinct and are discarded after every invocation, including faults.
public sealed class AlsRigTraversal
{
    private readonly AlsRigInstruction[] _instructions;
    private readonly AlsRigBranch[] _branches;
    private readonly Dictionary<string, int> _entries;
    private readonly int _maximumVisits, _maximumDepth;
    public IReadOnlyList<AlsRigInstruction> Instructions { get; }
    public IReadOnlyList<AlsRigBranch> Branches { get; }
    private static readonly HashSet<string> Supported = ["Execute", "Copy", "Zero", "JumpForward", "JumpBackward", "JumpAbsolute", "JumpToBranch", "RunInstructions", "Exit"];

    public AlsRigTraversal(IEnumerable<AlsRigInstruction> instructions, IEnumerable<AlsRigBranch> branches,
        IEnumerable<KeyValuePair<string, int>> entries, int maximumVisits = 8192, int maximumDepth = 32)
    {
        ArgumentNullException.ThrowIfNull(instructions); ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(entries);
        if (maximumVisits <= 0 || maximumDepth <= 0) throw new ArgumentException("Invalid Rig execution limits.");
        _maximumVisits = maximumVisits; _maximumDepth = maximumDepth;
        _instructions = instructions.Select(op => op with { Operands = Array.AsReadOnly(op.Operands.ToArray()) }).ToArray();
        _branches = branches.ToArray(); _entries = entries.ToDictionary(e => e.Key, e => e.Value);
        Instructions = Array.AsReadOnly(_instructions); Branches = Array.AsReadOnly(_branches);
        bool Valid(int index) => (uint)index < _instructions.Length;
        bool ValidJump(int index) => (uint)index <= _instructions.Length;
        if (_instructions.Length == 0 || _entries.Count == 0 || _entries.Any(e => string.IsNullOrWhiteSpace(e.Key) || !Valid(e.Value)))
            throw new ArgumentException("Invalid Rig entries.");
        for (int i = 0; i < _instructions.Length; i++)
        {
            var op = _instructions[i]; var code = op.Opcode;
            if (op.Index != i || !Supported.Contains(code) || !Enum.IsDefined(op.Dispatch) ||
                op.Operands.Any(o => o.Memory is < 0 or > 2 || string.IsNullOrWhiteSpace(o.Name) || o.Path is null) ||
                code == "RunInstructions" && (!Valid(op.First) || !Valid(op.Last) || op.First > op.Last) ||
                code is "Zero" or "RunInstructions" or "JumpToBranch" && op.Operands.Count != 1 ||
                code == "Copy" && op.Operands.Count != 2 ||
                op.Dispatch == AlsRigDispatch.ControlFlowBranch && (code != "Execute" || op.Operands.Count != 2) ||
                op.Dispatch == AlsRigDispatch.LazyIf && (code != "Execute" || op.Operands.Count < 3) ||
                code is "JumpForward" or "JumpBackward" or "JumpAbsolute" && !ValidJump(code == "JumpForward" ? i + op.Distance : code == "JumpBackward" ? i - op.Distance : op.Distance))
                throw new ArgumentException("Invalid Rig instruction: " + i);
        }
        foreach (var group in _instructions.Where(op => op.Opcode == "RunInstructions").GroupBy(op => op.Operands[0]))
            if (group.Select(op => (op.First, op.Last)).Distinct().Count() != 1)
                throw new ArgumentException("Rig execution-state register has conflicting dependency ranges.");
        var keys = new HashSet<(int, int, string)>();
        for (int i = 0; i < _branches.Length; i++)
        {
            var b = _branches[i];
            if (b.Index != i || !Valid(b.Instruction) || !Valid(b.First) || b.Last < b.First - 1 ||
                b.Last >= _instructions.Length || string.IsNullOrWhiteSpace(b.Label) ||
                !keys.Add((b.Instruction, b.Argument, b.Label.ToUpperInvariant())))
                throw new ArgumentException("Invalid Rig branch.");
        }
        foreach (var op in _instructions.Where(op => op.Opcode == "JumpToBranch"))
            if ((uint)op.FirstBranch >= _branches.Length || _branches[op.FirstBranch].Instruction != op.Index ||
                !_branches.Skip(op.FirstBranch).TakeWhile(b => b.Instruction == op.Index).Any(b => b.Argument == -1 && b.Label == "Completed"))
                throw new ArgumentException("Incomplete Rig named branch.");
    }

    public int[] Execute(string entry, IAlsRigInstructionExecutor units)
    {
        if (!_entries.TryGetValue(entry, out int start)) throw new ArgumentException("Unknown Rig entry.", nameof(entry));
        ArgumentNullException.ThrowIfNull(units);
        var visits = new List<int>(); var runState = new HashSet<AlsRigOperand>(); var lazyState = new HashSet<int>();
        int depth = 0;
        void Range(int first, int last)
        {
            if (++depth > _maximumDepth) throw new InvalidOperationException("Recursive Rig dependency.");
            try
            {
                for (int pc = first; pc <= last;)
                {
                    if ((uint)pc >= _instructions.Length || visits.Count >= _maximumVisits) throw new InvalidOperationException("Invalid or cyclic Rig control flow.");
                    var op = _instructions[pc]; visits.Add(pc);
                    switch (op.Opcode)
                    {
                        case "Copy": units.Copy(op.Operands[0], op.Operands[1]); pc++; break;
                        case "Zero": units.Zero(op.Operands[0]); pc++; break;
                        case "Exit": return;
                        case "JumpForward": pc += op.Distance; break;
                        case "JumpBackward": pc -= op.Distance; break;
                        case "JumpAbsolute": pc = op.Distance; break;
                        case "RunInstructions":
                            if (runState.Add(op.Operands[0])) Range(op.First, op.Last);
                            pc++; break;
                        case "JumpToBranch":
                            string label = units.ReadName(op.Operands[0]);
                            var branches = _branches.Skip(op.FirstBranch).TakeWhile(b => b.Instruction == pc);
                            var selected = branches.FirstOrDefault(b => b.Label.Equals(label, StringComparison.OrdinalIgnoreCase)) ??
                                branches.FirstOrDefault(b => b.Label == "Completed") ?? throw new InvalidOperationException("Missing Rig named branch.");
                            pc = selected.First; break;
                        case "Execute":
                            if (op.Dispatch == AlsRigDispatch.ControlFlowBranch)
                            {
                                string old = units.ReadName(op.Operands[1]);
                                units.WriteName(op.Operands[1], old.Equals("None", StringComparison.OrdinalIgnoreCase) ?
                                    (units.ReadBool(op.Operands[0]) ? "True" : "False") : "Completed");
                            }
                            else
                            {
                                if (op.Dispatch == AlsRigDispatch.LazyIf)
                                {
                                    int argument = units.ReadBool(op.Operands[0]) ? 1 : 2;
                                    var branch = _branches.FirstOrDefault(b => b.Instruction == pc && b.Argument == argument);
                                    if (branch is not null && lazyState.Add(branch.Index)) Range(branch.First, branch.Last);
                                }
                                units.Execute(op);
                            }
                            pc++; break;
                        default: throw new NotSupportedException(op.Opcode);
                    }
                }
            }
            finally { depth--; }
        }
        Range(start, _instructions.Length - 1);
        return visits.ToArray();
    }
}

using System.Text.Json;
using GodotAls.Core.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRigMemoryTests
{
    private readonly record struct Target(AlsDoubleVector Point) : IAlsRigStructValue
    {
        public object ReadField(string name) => name == "Point" ? Point : throw new NotSupportedException();
        public IAlsRigStructValue WithField(string name, object value) => name == "Point" ?
            this with { Point = (AlsDoubleVector)value } : throw new NotSupportedException();
    }
    private static AlsRigMemory Memory() => new([
        new(0, "pose", typeof(AlsPrecisePose), AlsPrecisePose.Identity),
        new(0, "target", typeof(Target), new Target(new(1, 2, 3))),
        new(0, "pin", typeof(float), 0f), new(0, "condition", typeof(bool), false),
        new(1, "literal", typeof(AlsDoubleVector), new AlsDoubleVector(1, 2, 3)),
        new(2, "external", typeof(double), 0d)]);

    [Fact]
    public void CancelledNestedWritesDoNotAffectTheCommittedPoseOrCustomStruct()
    {
        var committed = Memory(); var candidate = committed.Clone();
        candidate.Write(0, "pose", 7d, "Translation.X");
        candidate.Write(0, "target", 8d, "Point.Y");
        Assert.Equal(0d, committed.Read(0, "pose", "Translation.X"));
        Assert.Equal(2d, committed.Read(0, "target", "Point.Y"));
        Assert.Equal(7d, candidate.Read(0, "pose", "Translation.X"));
        Assert.Equal(8d, candidate.Read(0, "target", "Point.Y"));
        var retry = committed.Clone(); retry.Write(0, "pose", 7d, "Translation.X");
        Assert.Equal(candidate.Read(0, "pose"), retry.Read(0, "pose"));
    }

    [Fact]
    public void WorkResetRetainsExternalCommittedValuesAndLiteralValues()
    {
        var initial = Memory(); var live = initial.Clone();
        live.Write(0, "pose", 5d, "Translation.Z"); live.Write(2, "external", 12f);
        live.ResetWork(initial);
        Assert.Equal(AlsPrecisePose.Identity, live.Read(0, "pose"));
        Assert.Equal(12d, live.Read(2, "external"));
        Assert.Equal(initial.Read(1, "literal"), live.Read(1, "literal"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("X")]
    public void LiteralRootAndSubpathsRejectWrites(string path)
    {
        var memory = Memory(); var before = memory.Read(1, "literal");
        Assert.Throws<InvalidOperationException>(() => memory.Write(1, "literal", 5d, path));
        Assert.Equal(before, memory.Read(1, "literal"));
    }

    [Fact]
    public void ForeignResetRejectsBeforeChangingAnyRegister()
    {
        var live = Memory(); live.Write(0, "pin", 5f);
        Assert.Throws<ArgumentException>(() => live.ResetWork(Memory()));
        Assert.Equal(5f, live.Read(0, "pin"));
    }

    [Fact]
    public void RejectedPathAndTypeWritesAreAtomic()
    {
        var memory = Memory(); var before = memory.Read(0, "pose");
        Assert.Throws<NotSupportedException>(() => memory.Write(0, "pose", 4d, "Translation.Bad"));
        Assert.Throws<InvalidCastException>(() => memory.Write(0, "pose", false, "Translation"));
        Assert.Throws<ArgumentException>(() => memory.Write(0, "condition", 1d));
        Assert.Throws<ArgumentException>(() => memory.Write(0, "unknown", 1d));
        Assert.Equal(before, memory.Read(0, "pose")); Assert.Equal(false, memory.Read(0, "condition"));
    }

    [Fact]
    public void DeclaredNumericPinsQuantizeAtTheStorageBoundary()
    {
        var memory = Memory(); memory.Write(0, "pin", .1d); memory.Write(2, "external", .1f);
        Assert.Equal(.1f, Assert.IsType<float>(memory.Read(0, "pin")));
        Assert.Equal((double).1f, Assert.IsType<double>(memory.Read(2, "external")));
        Assert.Throws<ArgumentException>(() => memory.Write(0, "pin", true));
    }

    [Fact]
    public void OpaqueImmutableDataOwnsItsDocumentLifetime()
    {
        using var document = JsonDocument.Parse("{\"value\":7}");
        var memory = new AlsRigMemory([new(0, "opaque", typeof(JsonElement), document.RootElement)]);
        document.Dispose();
        Assert.Equal(7, ((JsonElement)memory.Clone().Read(0, "opaque")).GetProperty("value").GetInt32());
    }

    [Fact]
    public void MutablePayloadsAndDuplicateDefinitionsCannotEnterSnapshots()
    {
        Assert.Throws<ArgumentException>(() => new AlsRigMemory([new(0, "array", typeof(int[]), new int[1])]));
        Assert.Throws<ArgumentException>(() => new AlsRigMemory([new(0, "a", typeof(float), 0f), new(0, "a", typeof(float), 1f)]));
    }
}

public sealed class AlsRigTraversalTests
{
    private static AlsRigOperand O(string name) => new(0, -1, -1, name, "");
    private static AlsRigInstruction Op(int index, string code, params AlsRigOperand[] operands) => new(index, code, "unit", operands);
    private static AlsRigTraversal Program(AlsRigInstruction[] ops, AlsRigBranch[]? branches = null,
        int maximumVisits = 8192, int maximumDepth = 32) => new(ops, branches ?? [],
            new Dictionary<string, int> { ["entry"] = 0 }, maximumVisits, maximumDepth);

    private sealed class Units : IAlsRigInstructionExecutor
    {
        public bool Condition = true;
        public string Name = "None";
        public readonly List<int> Executed = [];
        public Action<AlsRigInstruction>? OnExecute;
        public void Copy(AlsRigOperand source, AlsRigOperand target) => Condition = true;
        public void Zero(AlsRigOperand operand) => Name = "None";
        public bool ReadBool(AlsRigOperand operand) => Condition;
        public string ReadName(AlsRigOperand operand) => Name;
        public void WriteName(AlsRigOperand operand, string value) => Name = value;
        public void Execute(AlsRigInstruction op) { Executed.Add(op.Index); OnExecute?.Invoke(op); }
    }

    [Fact]
    public void WorkDependencyRunsOnceButLazyArgumentHasItsOwnCache()
    {
        var state = O("state");
        var p = Program([
            Op(0, "RunInstructions", state) with { First = 6, Last = 6 },
            Op(1, "RunInstructions", state) with { First = 6, Last = 6 },
            Op(2, "Execute", O("condition"), O("a"), O("b")) with { Dispatch = AlsRigDispatch.LazyIf },
            Op(3, "JumpAbsolute") with { Distance = 8 }, Op(4, "Exit"), Op(5, "Exit"),
            Op(6, "Execute"), Op(7, "Exit"), Op(8, "Exit")],
            [new(0, 2, 1, "true", 6, 6)]);
        var units = new Units();
        Assert.Equal(new[] { 0, 6, 1, 2, 6, 3, 8 }, p.Execute("entry", units));
        Assert.Equal(new[] { 6, 6, 2 }, units.Executed);
        p.Execute("entry", units); Assert.Equal(4, units.Executed.Count(i => i == 6));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NamedBranchLoopsBackThroughCompletedInsteadOfRepeatingTheBody(bool condition)
    {
        var p = Program([
            Op(0, "Execute", O("condition"), O("flow")) with { Dispatch = AlsRigDispatch.ControlFlowBranch },
            Op(1, "JumpToBranch", O("flow")) with { FirstBranch = 0 }, Op(2, "Execute"),
            Op(3, "JumpAbsolute") with { Distance = 0 }, Op(4, "Execute"),
            Op(5, "JumpAbsolute") with { Distance = 0 }, Op(6, "Exit")],
            [new(0, 1, -1, "True", 2, 3), new(1, 1, -1, "False", 4, 5), new(2, 1, -1, "Completed", 6, 6)]);
        var units = new Units { Condition = condition };
        Assert.Equal(condition ? new[] { 0, 1, 2, 3, 0, 1, 6 } : new[] { 0, 1, 4, 5, 0, 1, 6 }, p.Execute("entry", units));
        Assert.Single(units.Executed); Assert.Equal("Completed", units.Name);
    }

    [Fact]
    public void LazyIfOnlyRunsTheSelectedArgumentOnceAndFaultRetryStartsFresh()
    {
        var p = Program([
            Op(0, "Copy", O("yes"), O("condition")),
            Op(1, "Execute", O("condition"), O("a"), O("b")) with { Dispatch = AlsRigDispatch.LazyIf },
            Op(2, "Execute"), Op(3, "JumpAbsolute") with { Distance = 1 }, Op(4, "Exit"),
            Op(5, "Exit"), Op(6, "Execute"), Op(7, "Execute")],
            [new(0, 1, 1, "true", 6, 6), new(1, 1, 2, "false", 7, 7)]);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var units = new Units(); int calls = 0;
            units.OnExecute = op => { if (op.Index == 2) units.Condition = false; if (op.Index == 1 && ++calls == 3) throw new ArithmeticException(); };
            Assert.Throws<ArithmeticException>(() => p.Execute("entry", units));
            Assert.Equal(1, units.Executed.Count(i => i == 6)); Assert.Equal(1, units.Executed.Count(i => i == 7));
        }
    }

    [Theory]
    [InlineData("tRuE", 1)]
    [InlineData("unknown", 3)]
    public void NamedSelectionIsCaseInsensitiveAndFallsBackToCompleted(string name, int target)
    {
        var p = Program([Op(0, "JumpToBranch", O("flow")) with { FirstBranch = 0 }, Op(1, "Execute"), Op(2, "Exit"), Op(3, "Exit")],
            [new(0, 0, -1, "True", 1, 2), new(1, 0, -1, "Completed", 3, 3)]);
        Assert.Equal(target, p.Execute("entry", new Units { Name = name })[1]);
    }

    [Fact]
    public void CallerOwnedDefinitionsCannotChangeAnAlreadyBoundProgram()
    {
        var operands = new[] { O("source"), O("target") };
        var ops = new[] { Op(0, "Copy", operands), Op(1, "Exit") };
        var entries = new Dictionary<string, int> { ["entry"] = 0 };
        var p = new AlsRigTraversal(ops, [], entries);
        operands[0] = O("changed"); ops[0] = Op(0, "Exit"); entries["entry"] = 1;
        Assert.Equal("source", p.Instructions[0].Operands[0].Name);
        Assert.Equal(new[] { 0, 1 }, p.Execute("entry", new Units()));
    }

    [Fact]
    public void CyclicJumpsAndDeepDependenciesRespectTheExecutionLimits()
    {
        Assert.Throws<InvalidOperationException>(() => Program([Op(0, "JumpAbsolute") with { Distance = 0 }], maximumVisits: 3).Execute("entry", new Units()));
        var deep = Program([Op(0, "RunInstructions", O("a")) with { First = 1, Last = 1 },
            Op(1, "RunInstructions", O("b")) with { First = 2, Last = 2 }, Op(2, "Execute")], maximumDepth: 2);
        Assert.Throws<InvalidOperationException>(() => deep.Execute("entry", new Units()));
    }

    [Fact]
    public void ConflictingWorkRangesAndIncompleteNamedBranchesAreRejectedBeforeExecution()
    {
        Assert.Throws<ArgumentException>(() => Program([Op(0, "RunInstructions", O("state")) with { First = 2, Last = 2 },
            Op(1, "RunInstructions", O("state")) with { First = 3, Last = 3 }, Op(2, "Exit"), Op(3, "Exit")]));
        Assert.Throws<ArgumentException>(() => Program([Op(0, "JumpToBranch", O("flow")) with { FirstBranch = 0 }, Op(1, "Exit")],
            [new(0, 0, -1, "True", 1, 1)]));
    }

    [Fact]
    public void InvalidOpcodeAndUnknownEntryNeverReachTheBackend()
    {
        Assert.Throws<ArgumentException>(() => Program([Op(0, "Unsupported")]));
        var units = new Units(); Assert.Throws<ArgumentException>(() => Program([Op(0, "Exit")]).Execute("wrong", units));
        Assert.Empty(units.Executed);
    }
}

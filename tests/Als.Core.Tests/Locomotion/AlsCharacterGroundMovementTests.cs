using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsCharacterGroundMovementTests
{
    private readonly record struct Hit(bool Blocking, bool Penetrating, bool CanStep, float Time,
        Vector3 Point, Vector3 Normal, Vector3 ImpactNormal, Vector3 Location) : IAlsCharacterGroundHit
    { public bool Valid => Blocking && !Penetrating; }
    private readonly record struct Floor(bool Blocking, bool Walkable, bool LineTrace, float FloorDistance,
        float LineDistance, Vector3 Point, Vector3 Normal) : IAlsCharacterGroundFloor;
    private readonly record struct Checkpoint(Vector3 Position, int Marker);
    private sealed class World : IAlsCharacterGroundWorld<Hit, Floor, int, Checkpoint>
    {
        public Vector3 Position { get; private set; } = new(0, .9215f, 0);
        public bool Recovered { get; private set; }
        public bool RecoverFirst;
        public int Marker, Captures, Restores, FloorReads, Adjustments;
        public Floor Current = new(true, true, false, .0215f, .0215f, Vector3.Zero, Vector3.UnitY);
        public Floor? StepFloor;
        public readonly Queue<Hit> Hits = new();
        public readonly List<(Vector3 Motion, bool Safe)> Moves = [];
        public Checkpoint Capture() { Captures++; return new(Position, Marker); }
        public void Restore(Checkpoint checkpoint) { Restores++; Position = checkpoint.Position; Marker = checkpoint.Marker; }
        public Hit Move(Vector3 motion, float halfHeight, bool safe)
        {
            Moves.Add((motion, safe)); Recovered = RecoverFirst && Moves.Count == 1; Marker++;
            var hit = Hits.Count == 0 ? Free : Hits.Dequeue();
            if (!hit.Penetrating) Position += motion * hit.Time;
            return hit;
        }
        public Floor Find(float halfHeight, bool walking) { FloorReads++; return FloorReads > 1 ? StepFloor ?? Current : Current; }
        public int AfterSweep(float halfHeight) { Adjustments++; return Marker; }
    }
    private static readonly Hit Free = new(false, false, false, 1, default, default, default, default);
    private static readonly Hit Wall = new(true, false, true, .5f, new(0, .2f, .5f), -Vector3.UnitZ,
        -Vector3.UnitZ, new(0, .9215f, .5f));
    private static readonly AlsCharacterGroundSettings Settings = new(.3f, .019f, .024f, .0015f, .45f, .7f);
    private static AlsCharacterGroundMovement<Hit, Floor, int, Checkpoint> Controller(World world) => new(world, Settings);
    private static AlsCharacterGroundMove<Hit, int> Walk(World world, bool root = false) => Controller(world).Walk(Vector3.UnitZ, Vector3.UnitZ * 5, 1, .9f, root);

    [Fact]
    public void FreeFloorMoveKeepsEndVelocityAndAdjustsFloorOnce()
    {
        var world = new World(); var move = Walk(world);
        Assert.Equal(new Vector3(0, .9215f, 1), world.Position);
        Assert.Equal(Vector3.UnitZ * 5, move.Velocity); Assert.Empty(move.Contacts);
        Assert.Equal(1, world.Adjustments); Assert.Equal(world.Marker, move.Floor);
    }

    [Theory]
    [InlineData(false, .75f)]
    [InlineData(true, 0f)]
    public void RampProjectionUsesSweepNormalAndLineTraceBypassesIt(bool line, float rise)
    {
        var world = new World { Current = new(true, true, line, .0215f, .0215f, default, new(0, .8f, -.6f)) };
        Walk(world); Assert.Equal(new Vector3(0, rise, 1), world.Moves[0].Motion);
    }

    [Fact]
    public void StepMovesUpForwardDownAndPreservesContactOrder()
    {
        var world = new World(); world.Hits.Enqueue(Wall); world.Hits.Enqueue(Free); world.Hits.Enqueue(Free);
        var landing = new Hit(true, false, true, .3584337f, new(0, .25f, 1), Vector3.UnitY, Vector3.UnitY, new(0, 1.1715f, 1));
        world.Hits.Enqueue(landing); var result = Walk(world);
        Assert.True(result.Stepped); Assert.False(result.StepReverted); Assert.Equal(new[] { Wall, landing }, result.Contacts);
        Assert.Equal(new[] { true, false, false, false }, world.Moves.Select(m => m.Safe));
        Assert.Equal(.4285f, world.Moves[1].Motion.Y, 6); Assert.Equal(.5f, world.Moves[2].Motion.Z);
        Assert.Equal(-.498f, world.Moves[3].Motion.Y, 6);
        Assert.Equal(Vector3.UnitZ, result.Velocity); Assert.Equal(1, world.Captures); Assert.Equal(0, world.Restores);
    }

    [Theory]
    [InlineData("up")]
    [InlineData("forward")]
    [InlineData("down")]
    [InlineData("edge")]
    [InlineData("height")]
    [InlineData("unwalkable")]
    [InlineData("floor")]
    public void FailedStepRestoresOpaqueCheckpointBeforeFloorAdjustment(string failure)
    {
        var world = new World(); world.Hits.Enqueue(Wall);
        var penetrating = Wall with { Penetrating = true, Time = 0 };
        world.Hits.Enqueue(failure == "up" ? penetrating : Free);
        if (failure != "up") world.Hits.Enqueue(failure == "forward" ? penetrating : Free);
        if (failure is not ("up" or "forward"))
        {
            var down = new Hit(true, failure == "down", true, .4f, new(failure == "edge" ? .5f : 0, failure == "height" ? .8f : .25f, 1),
                Vector3.UnitY, failure == "unwalkable" ? -Vector3.UnitZ : Vector3.UnitY, new(0, 1.1715f, 1));
            world.Hits.Enqueue(down);
        }
        if (failure == "floor") world.StepFloor = world.Current with { Blocking = false };
        var result = Walk(world);
        Assert.False(result.Stepped); Assert.True(result.StepReverted); Assert.Equal(1, world.Restores);
        Assert.Equal(new Vector3(0, .9215f, .5f), world.Position); Assert.Equal(1, world.Marker);
        Assert.Equal(Vector3.UnitZ * .5f, result.Velocity); Assert.Equal(1, world.Adjustments);
    }

    [Fact]
    public void NonStepBodySlidesWithoutTakingCheckpoint()
    {
        var world = new World(); world.Hits.Enqueue(Wall with { CanStep = false }); var result = Walk(world);
        Assert.False(result.Stepped); Assert.Equal(0, world.Captures); Assert.Equal(Vector3.UnitZ * .5f, result.Velocity);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RootOrPenetrationRecoveryKeepsRequestedEndVelocity(bool root, bool recovered)
    {
        var world = new World { RecoverFirst = recovered }; world.Hits.Enqueue(Wall with { CanStep = false });
        Assert.Equal(Vector3.UnitZ * 5, Walk(world, root).Velocity);
    }

    [Fact]
    public void OpposingWallsStopHorizontalCornerMotion()
    {
        var world = new World(); world.Hits.Enqueue(Wall with { CanStep = false, Normal = -Vector3.UnitX, ImpactNormal = -Vector3.UnitX });
        world.Hits.Enqueue(Wall with { CanStep = false });
        var result = Controller(world).Walk(new(1, 0, 1), new(5, 0, 5), 1, .9f, false);
        Assert.Equal(new Vector3(.5f, 0, .75f), result.Velocity); Assert.Equal(2, world.Moves.Count);
        Assert.Equal(2, result.Contacts.Length);
    }

    [Fact]
    public void ParallelWallsApplySmallEscapeOffsetThenContinueTangent()
    {
        var world = new World(); var wall = Wall with { CanStep = false, Normal = -Vector3.UnitX, ImpactNormal = -Vector3.UnitX };
        world.Hits.Enqueue(wall); world.Hits.Enqueue(wall);
        var result = Controller(world).Walk(new(1, 0, 1), new(5, 0, 5), 1, .9f, false);
        Assert.Equal(3, world.Moves.Count); Assert.Equal(-.0001f, world.Moves[2].Motion.X);
        Assert.Equal(.25f, world.Moves[2].Motion.Z); Assert.Equal(.4999f, result.Velocity.X, 6); Assert.Equal(1, result.Velocity.Z);
    }

    [Fact]
    public void MissingWalkableFloorSkipsSweepButKeepsFloorAdjustment()
    {
        var world = new World { Current = new(false, false, false, 1, 1, default, default) };
        Walk(world); Assert.Empty(world.Moves); Assert.Equal(1, world.Adjustments);
    }

    [Fact]
    public void ConsecutiveFramesDoNotLeakContactsOrTeleportFlag()
    {
        var world = new World { RecoverFirst = true }; var controller = Controller(world);
        world.Hits.Enqueue(Wall with { CanStep = false }); controller.Walk(Vector3.UnitZ, Vector3.UnitZ * 5, 1, .9f, false);
        var next = controller.Walk(Vector3.UnitZ, Vector3.UnitZ * 5, 1, .9f, false);
        Assert.Empty(next.Contacts); Assert.False(next.StepReverted); Assert.Equal(Vector3.UnitZ * 5, next.Velocity);
    }

    [Fact]
    public void InvalidSettingsOrFrameAreRejectedBeforeQueries()
    {
        var world = new World();
        Assert.Throws<ArgumentException>(() => new AlsCharacterGroundMovement<Hit, Floor, int, Checkpoint>(world, Settings with { Radius = 0 }));
        var controller = Controller(world);
        Assert.Throws<ArgumentException>(() => controller.Walk(Vector3.Zero, Vector3.Zero, 0, .9f, false));
        Assert.Throws<ArgumentException>(() => controller.Walk(Vector3.Zero, Vector3.Zero, 1, .2f, false));
        Assert.Throws<ArgumentException>(() => controller.Walk(new(float.NaN, 0, 0), Vector3.Zero, 1, .9f, false));
        Assert.Equal(0, world.FloorReads); Assert.Empty(world.Moves);
    }
}

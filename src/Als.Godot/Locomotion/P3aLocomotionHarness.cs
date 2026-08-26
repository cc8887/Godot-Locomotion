using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class P3aLocomotionHarness : Node
{
    private AlsP3aHarnessContext _context = null!;
    private bool _quitting;

    public override void _Ready()
    {
        try
        {
            if (!TryReadArguments(out var mode, out var characterCount))
            {
                GetTree().Quit(2);
                return;
            }

            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = 0;
            var settingsJson = Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json");
            var settings = AlsLocomotionSettings.Load(settingsJson);
            var motorSettings = new AlsMotorSettings(
                capsuleRadius: 0.35f,
                standingHeight: settings.StandingHalfHeight * 2f,
                crouchingHeight: settings.CrouchedHalfHeight * 2f,
                settings.Standing,
                settings.Crouching,
                settings.InitialMaxAcceleration,
                settings.InitialMaxBrakingDeceleration,
                settings.Gravity,
                settings.JumpSpeed,
                collisionMask: 1,
                settings.VelocityAngleInterpolationStart,
                settings.VelocityAngleInterpolationEnd);
            _context = new AlsP3aHarnessContext(
                mode,
                characterCount,
                System.Environment.CurrentManagedThreadId,
                settings,
                motorSettings);

            AddChild(CreateFloor());
            for (var index = 0; index < characterCount; index++)
            {
                AddCharacter(index);
            }
            _context.SpareEntry = CreateCharacter(
                new AlsSlotHandle(0, 2),
                active: false);

            var commit = new AlsP3aCommitStage { Name = "Commit" };
            commit.Configure(_context, this);
            AddChild(commit);
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_quitting || _context is null)
        {
            return;
        }

        try
        {
            if (Math.Abs(delta - AlsP3aHarnessContext.DeltaTime) > 1e-9)
            {
                throw new InvalidOperationException($"Physics delta must be 1/60, received {delta:R}.");
            }

            var frameId = Volatile.Read(ref _context.PublishedFrameId) + 1;
            if (frameId > AlsP3aHarnessContext.TotalFrames)
            {
                return;
            }

            var measure = frameId > AlsP3aHarnessContext.WarmupFrames;
            var allocatedBefore = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            for (var index = 0; index < _context.Entries.Length; index++)
            {
                var entry = _context.Entries[index];
                entry.PendingInput = entry.Motor.Step(
                    frameId,
                    checked((int)entry.Handle.CharacterId),
                    checked((int)entry.Handle.Generation),
                    AlsP3aHarnessContext.DeltaTime);
                entry.LastMotorFrameId = entry.PendingInput.Identity.FrameId;
                if (index == 0)
                {
                    ObserveMeasurementTraversal(entry, measure);
                }
            }
            if (measure)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                if (allocated != 0 && _context.FirstGatherAllocationFrame == 0)
                {
                    _context.FirstGatherAllocationFrame = frameId;
                }
                Interlocked.Add(
                    ref _context.GatherMotorAllocations,
                    allocated);
            }

            var exchangeBefore = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            for (var index = 0; index < _context.Entries.Length; index++)
            {
                var entry = _context.Entries[index];
                entry.Exchange.PublishInput(entry.PendingInput);
            }
            if (measure)
            {
                Interlocked.Add(
                    ref _context.ExchangeAllocations,
                    GC.GetAllocatedBytesForCurrentThread() - exchangeBefore);
            }

            Volatile.Write(ref _context.PublishedFrameId, frameId);
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    public void ReplaceCharacterZero()
    {
        var oldEntry = _context.Entries[0];
        SetCharacterActive(oldEntry, active: false);
        if (!_context.Registry.Release(oldEntry.Handle))
        {
            throw new InvalidOperationException("Failed to release the P3A character slot.");
        }

        var acquiredHandle = _context.Registry.Acquire();
        var newEntry = _context.SpareEntry;
        if (acquiredHandle != newEntry.Handle ||
            acquiredHandle.CharacterId != oldEntry.Handle.CharacterId ||
            acquiredHandle.Generation != oldEntry.Handle.Generation + 1)
        {
            throw new InvalidOperationException("P3A slot generation was not incremented on reuse.");
        }

        var staleIdentity = new AlsFrameIdentity(
            AlsP3aHarnessContext.ReplacementFrame + 1,
            oldEntry.Handle.CharacterId,
            oldEntry.Handle.Generation);
        oldEntry.Exchange.PublishResult(AlsFrameResult.CreateDefault(staleIdentity));
        var currentIdentity = new AlsFrameIdentity(
            staleIdentity.FrameId,
            staleIdentity.CharacterId,
            newEntry.Handle.Generation);
        var currentGenerationRejected =
            !oldEntry.Exchange.TryConsumeResult(currentIdentity, out _);
        var oldGenerationPreserved =
            oldEntry.Exchange.TryConsumeResult(staleIdentity, out var staleResult) &&
            staleResult.Identity == staleIdentity;
        _context.OldGenerationRejected = currentGenerationRejected && oldGenerationPreserved;
        _context.Entries[0] = newEntry;
        SetCharacterActive(newEntry, active: true);
        _context.ReplacementHandle = newEntry.Handle;
        _context.ReplacementCount++;
    }

    public void Fail(string message)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        GD.PushError($"GODOT_ALS_P3A_FAIL {message}");
        GetTree().Quit(1);
    }

    private AlsP3aHarnessEntry AddCharacter(int entryIndex)
    {
        var entry = CreateCharacter(_context.Registry.Acquire(), active: true);
        _context.Entries[entryIndex] = entry;
        return entry;
    }

    private AlsP3aHarnessEntry CreateCharacter(AlsSlotHandle handle, bool active)
    {
        var entry = new AlsP3aHarnessEntry(handle);
        var motor = new AlsCharacterMotor
        {
            Name = $"Motor_{entry.Handle.CharacterId}_Generation_{entry.Handle.Generation}",
            Position = new Vector3(
                ((float)entry.Handle.CharacterId - ((_context.Entries.Length - 1) * 0.5f)) * 8f,
                _context.MotorSettings.StandingHeight * 0.5f,
                0f),
        };
        AddChild(motor);
        motor.Configure(_context.MotorSettings, AlsMotorReplay.CreateHarnessSequence());
        entry.Motor = motor;

        var worker = new AlsP3aWorkerRoot
        {
            Name = $"Worker_{entry.Handle.CharacterId}_Generation_{entry.Handle.Generation}",
        };
        worker.Configure(_context, entry);
        entry.Worker = worker;
        AddChild(worker);
        SetCharacterActive(entry, active);
        return entry;
    }

    private void SetCharacterActive(AlsP3aHarnessEntry entry, bool active)
    {
        entry.Motor.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        entry.Motor.CollisionLayer = active ? 1u : 0u;
        entry.Motor.CollisionMask = active ? _context.MotorSettings.CollisionMask : 0u;
        entry.Worker.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    private void ObserveMeasurementTraversal(AlsP3aHarnessEntry entry, bool measure)
    {
        var grounded = entry.PendingInput.Floor.IsGrounded;
        if (measure)
        {
            var command = entry.PendingInput.Command;
            _context.MeasurementCoverage |= command.RequestedGait switch
            {
                AlsGait.Walking => AlsP3aHarnessContext.CoverageWalking,
                AlsGait.Running => AlsP3aHarnessContext.CoverageRunning,
                AlsGait.Sprinting => AlsP3aHarnessContext.CoverageSprinting,
                _ => 0,
            };
            if (entry.PendingInput.Stance == AlsStance.Crouching)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageCrouching;
            }
            if (entry.PendingInput.JumpAccepted == 1)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageJump;
            }
            if (grounded == 0)
            {
                _context.MeasurementSawAirborne = true;
            }
            if (_context.MeasurementSawAirborne && entry.PreviousGrounded == 0 && grounded == 1)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageLand;
            }
            if (command.MovementAxes.Y > 0f)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageForward;
            }
            if (command.MovementAxes.X > 0f)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageRight;
            }
            if (command.MovementAxes.Y < 0f)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageBackward;
            }
            if (command.MovementAxes.X < 0f)
            {
                _context.MeasurementCoverage |= AlsP3aHarnessContext.CoverageLeft;
            }
            _context.MeasurementCoverage |= entry.PendingInput.RotationMode switch
            {
                AlsRotationMode.LookingDirection => AlsP3aHarnessContext.CoverageLookingDirection,
                AlsRotationMode.VelocityDirection => AlsP3aHarnessContext.CoverageVelocityDirection,
                AlsRotationMode.Aiming => AlsP3aHarnessContext.CoverageAiming,
                _ => 0,
            };
        }
        entry.PreviousGrounded = grounded;
    }

    private static StaticBody3D CreateFloor()
    {
        var floor = new StaticBody3D
        {
            Name = "Floor",
            Position = new Vector3(0f, -0.5f, 0f),
            CollisionLayer = 1,
            CollisionMask = 1,
        };
        floor.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(400f, 1f, 400f) },
        });
        return floor;
    }

    private static bool TryReadArguments(out AlsHarnessMode mode, out int characterCount)
    {
        mode = default;
        characterCount = 0;
        var sawMode = false;
        var sawCharacters = false;

        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--als-mode=", StringComparison.Ordinal) && !sawMode)
            {
                var value = argument["--als-mode=".Length..];
                if (value == "single")
                {
                    mode = AlsHarnessMode.Single;
                }
                else if (value == "parallel")
                {
                    mode = AlsHarnessMode.Parallel;
                }
                else
                {
                    GD.PushError($"Invalid P3A harness mode: {value}");
                    return false;
                }
                sawMode = true;
            }
            else if (argument.StartsWith("--als-characters=", StringComparison.Ordinal) && !sawCharacters)
            {
                var value = argument["--als-characters=".Length..];
                if (!int.TryParse(value, out characterCount) || characterCount is not (1 or 10))
                {
                    GD.PushError($"Invalid P3A character count: {value}");
                    return false;
                }
                sawCharacters = true;
            }
            else
            {
                GD.PushError($"Unknown or duplicate P3A harness argument: {argument}");
                return false;
            }
        }

        if (!sawMode || !sawCharacters)
        {
            GD.PushError("P3A harness requires exactly one mode and character-count argument.");
            return false;
        }

        return true;
    }
}

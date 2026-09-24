using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;

namespace GodotAls.Locomotion;

public partial class AlsP3CharacterSlot : Node
{
    private readonly AlsSlotRegistry _registry = new(1);
    private readonly AlsP3ExchangeSlot _exchangeSlot = new();
    private readonly AlsP4FootProbeExchange _footProbeExchange = new();
    private AlsP3RuntimeContext _context = null!;
    private Func<IAlsLocomotionCommandSource> _commandSourceFactory = null!;
    private AlsP3Character _active = null!;
    private AlsP3Character? _spare;
    private AlsP3ReplacementPhase _replacementPhase;
    private long _replacementCompletedFrameId;
    private AlsFrameIdentity _retiredResultIdentity;
    private AlsFrameInput _retiredMotorInput;
    private bool _retiredResultObserved;
    private bool _retiredNodeReleased;
    private bool _generationMismatchObserved;
    private long _committedFrameAtClassification;
    private bool _recoveryCommitted;
    private bool _failureRecoveryRequested;
    private long _classificationSequenceBaseline;
    private AlsFrameIdentity _expectedClassificationIdentity;
    private bool _classificationRequired;
    private bool _configured;
    private int _disposed;

    public AlsP3CharacterSlot()
    {
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Lifecycle;
    }

    public AlsP3Character ActiveCharacter
    {
        get
        {
            EnsureConfigured();
            ThrowIfDisposed();
            return _active;
        }
    }

    public AlsP3SlotReplacementDiagnostics ReplacementDiagnostics
    {
        get
        {
            EnsureMainThread();
            return new AlsP3SlotReplacementDiagnostics(
                _replacementPhase != AlsP3ReplacementPhase.None,
                _retiredResultObserved,
                _retiredResultIdentity,
                _retiredNodeReleased,
                _generationMismatchObserved,
                _committedFrameAtClassification,
                _recoveryCommitted,
                _replacementPhase,
                CountVisibleCharacters())
            {
                RetiredMotorInput = _retiredMotorInput,
            };
        }
    }

    public void Configure(
        AlsP3RuntimeContext context,
        Func<IAlsLocomotionCommandSource> commandSourceFactory,
        Vector3 characterPosition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandSourceFactory);
        EnsureMainThread();
        ThrowIfDisposed();
        if (!IsInsideTree())
        {
            throw new InvalidOperationException(
                "P3 character slot must be in the scene tree before Configure().");
        }
        if (_configured)
        {
            throw new InvalidOperationException("P3 character slot is already configured.");
        }

        _context = context;
        _commandSourceFactory = commandSourceFactory;
        var activeHandle = _registry.Acquire();
        var spareHandle = new AlsSlotHandle(
            activeHandle.CharacterId,
            NextGeneration(activeHandle.Generation));
        try
        {
            _active = CreateCharacter(activeHandle, characterPosition, active: true);
            _spare = CreateCharacter(spareHandle, characterPosition, active: false);
            _configured = true;
            ValidateVisibilityInvariants();
        }
        catch
        {
            Interlocked.Exchange(ref _disposed, 1);
            DisposeRuntimeCore(freeNodes: true);
            throw;
        }
    }

    public void RequestReplacement(long completedFrameId)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        ArgumentOutOfRangeException.ThrowIfNegative(completedFrameId);
        if (completedFrameId >
            AlsP3VisualRootVisibilityObservation.MaximumResumableCompletedFrameId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedFrameId),
                completedFrameId,
                "P3 replacement frame is too large to resume safely.");
        }
        if (_replacementPhase != AlsP3ReplacementPhase.None)
        {
            throw new InvalidOperationException("P3 slot replacement is already in progress.");
        }

        _active.BeginReplacementRequest(completedFrameId);
        _replacementCompletedFrameId = completedFrameId;
        _replacementPhase = AlsP3ReplacementPhase.AwaitingRetiredResult;
        ValidateVisibilityInvariants();
    }

    public void RequestFailureRecovery()
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        if (_replacementPhase is not (AlsP3ReplacementPhase.None or
            AlsP3ReplacementPhase.Complete))
        {
            throw new InvalidOperationException(
                "P3 slot replacement is already in progress.");
        }
        if (!_active.IsPoseFrozen || _active.WorkerInFlight != 0)
        {
            throw new InvalidOperationException(
                "P3 failure recovery requires a frozen idle generation.");
        }
        PrepareFailureRecoverySpare();

        _replacementCompletedFrameId = _active.RuntimeCommittedFrameId;
        _recoveryCommitted = false;
        _failureRecoveryRequested = true;
        ReplaceActiveCharacter();
        _replacementPhase = AlsP3ReplacementPhase.AwaitingGenerationMismatch;
        ValidateVisibilityInvariants();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_configured || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        ValidateVisibilityInvariants();
        switch (_replacementPhase)
        {
            case AlsP3ReplacementPhase.AwaitingRetiredResult:
                TryReplaceAfterRetiredResult();
                break;
            case AlsP3ReplacementPhase.AwaitingGenerationMismatch:
                TryStartRecovery();
                break;
            case AlsP3ReplacementPhase.AwaitingRecoveryCommit:
                TryCompleteRecovery();
                break;
        }
        ValidateVisibilityInvariants();
    }

    public void DisposeRuntime()
    {
        EnsureMainThread();
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        DeactivateCharactersForDisposal();
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }
        DisposeRuntimeCore(freeNodes: true);
    }

    public override void _ExitTree()
    {
        if (GodotThread.IsMainThread() && Volatile.Read(ref _disposed) == 0)
        {
            DeactivateCharactersForDisposal();
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                DisposeRuntimeCore(freeNodes: false);
            }
        }
    }

    private AlsP3Character CreateCharacter(
        AlsSlotHandle handle,
        Vector3 characterPosition,
        bool active)
    {
        var commandSource = _commandSourceFactory()
            ?? throw new InvalidOperationException("P3 command-source factory returned null.");
        var character = new AlsP3Character
        {
            Name = $"Character_{handle.CharacterId}_Generation_{handle.Generation}",
            Position = characterPosition,
            Visible = false,
        };
        AddChild(character);
        try
        {
            character.Configure(
                _context,
                handle,
                commandSource,
                _exchangeSlot,
                _footProbeExchange);
            character.SetSchedulingActive(active);
            return character;
        }
        catch
        {
            character.Visible = false;
            RemoveChild(character);
            character.Free();
            throw;
        }
    }

    private void TryReplaceAfterRetiredResult()
    {
        var expectedIdentity = _active.HandleIdentity(_replacementCompletedFrameId + 1);
        if (!_exchangeSlot.TryGetPublishedIdentity(out var publishedIdentity) ||
            publishedIdentity != expectedIdentity)
        {
            return;
        }
        if (_active.WorkerInFlight != 0)
        {
            throw new InvalidOperationException(
                "P3 slot replacement reached Order 3 with a worker callback still in flight.");
        }

        _retiredResultIdentity = publishedIdentity;
        _retiredResultObserved = true;
        _failureRecoveryRequested = false;
        ReplaceActiveCharacter();
        _replacementPhase = AlsP3ReplacementPhase.AwaitingGenerationMismatch;
    }

    private void ReplaceActiveCharacter()
    {
        var retired = _active;
        _retiredMotorInput = retired.LatestMotorInput;
        var publishedMotorFrameId = retired.PublishedFrameId;
        if (publishedMotorFrameId <= _replacementCompletedFrameId ||
            _retiredMotorInput.Identity != retired.HandleIdentity(publishedMotorFrameId))
        {
            throw new InvalidOperationException(
                "P3 retired character did not expose one latest published Motor input.");
        }
        var motorCheckpoint = retired.CapturePublishedMotorLifecycle(
            publishedMotorFrameId);
        _classificationRequired = !_failureRecoveryRequested ||
            _exchangeSlot.TryGetPublishedIdentity(out _);
        var workerCheckpoint = _failureRecoveryRequested
            ? retired.CaptureFailureWorkerLifecycle(_replacementCompletedFrameId)
            : default;
        _replacementCompletedFrameId = publishedMotorFrameId - 1;
        retired.RetireForReplacement();
        if (retired.Visible)
        {
            throw new InvalidOperationException(
                "P3 retired character remained visible before registry release.");
        }
        if (!_registry.Release(retired.Handle))
        {
            throw new InvalidOperationException("P3 slot failed to release its retired generation.");
        }
        var replacementHandle = _registry.Acquire();
        if (_spare is null || _spare.Handle != replacementHandle)
        {
            throw new InvalidOperationException(
                "P3 prebuilt replacement generation did not match the slot registry.");
        }

        var replacement = _spare;
        var rotation = _retiredMotorInput.CharacterRotation;
        if (rotation.Applied == 1 && rotation.FeedbackIdentity.SlotGeneration != 0)
        {
            var previous = rotation.FeedbackIdentity;
            if (previous.CharacterId != _retiredMotorInput.Identity.CharacterId ||
                previous.SlotGeneration != _retiredMotorInput.Identity.SlotGeneration ||
                previous.FrameId >= publishedMotorFrameId)
                throw new InvalidOperationException("Cannot migrate foreign or uncommitted rotation history.");
            // This input already includes the retired Motor's rotation. Transfer
            // its history with the checkpoint, without evaluating rotation again.
            // RetiredMotorInput keeps the original provenance for diagnostics.
            rotation = rotation with { FeedbackIdentity = replacement.HandleIdentity(previous.FrameId) };
        }
        var stagedMotorInput = _retiredMotorInput with
        {
            Identity = replacement.HandleIdentity(publishedMotorFrameId),
            CharacterRotation = rotation,
            // Movement is already integrated, but the retired actor's command
            // must never start an action on a fresh generation.
            ActionRequest = _retiredMotorInput.ActionRequest == AlsActionRequest.None
                ? AlsActionRequest.None // Preserve the legacy command source's unscoped sentinel.
                : AlsActionRequest.None with { SlotGeneration = replacement.Handle.Generation },
            ActionParameters = default,
            MovementAction = default,
            GameplayAction = _retiredMotorInput.GameplayAction == AlsTimelineAction.GettingUp
                ? AlsTimelineAction.None : _retiredMotorInput.GameplayAction,
        };
        var releasePlatformOnNextStep =
            stagedMotorInput.Floor.PlatformId >= 0 ||
            stagedMotorInput.LeftFootHit.PlatformId >= 0 ||
            stagedMotorInput.RightFootHit.PlatformId >= 0;
        replacement.RestorePublishedMotorLifecycle(
            in motorCheckpoint,
            publishedMotorFrameId,
            releasePlatformOnNextStep);
        if (_failureRecoveryRequested)
        {
            replacement.RestoreFailureWorkerLifecycle(
                in workerCheckpoint,
                releasePlatformOnNextStep ? publishedMotorFrameId + 1 : -1);
        }
        replacement.StageReplacementMotorInput(in stagedMotorInput);
        replacement.ResetVisualReady();
        _spare = null;
        if (retired.Visible)
        {
            throw new InvalidOperationException(
                "P3 retired character became visible before replacement activation.");
        }
        _active = replacement;
        if (_classificationRequired)
        {
            _active.StartReplacementClassification(_replacementCompletedFrameId);
        }
        else
        {
            _active.StartReplacementWithoutClassification(_replacementCompletedFrameId);
        }
        var classification = _active.ResultClassificationDiagnostics;
        _classificationSequenceBaseline = classification.Sequence;
        _expectedClassificationIdentity = _active.HandleIdentity(
            _replacementCompletedFrameId + 1);

        retired.DisposeRuntime();
        retired.DisposeRuntime();
        RemoveChild(retired);
        retired.Free();
        _retiredNodeReleased = !GodotObject.IsInstanceValid(retired);
        if (!_retiredNodeReleased)
        {
            throw new InvalidOperationException("P3 retired character remained valid after Free().");
        }
    }

    private void TryStartRecovery()
    {
        var classification = _active.ResultClassificationDiagnostics;
        if (!_classificationRequired)
        {
            if (_active.PublishedFrameId != _replacementCompletedFrameId + 1)
            {
                return;
            }
            if (classification.Sequence != _classificationSequenceBaseline ||
                _active.RuntimeCommittedFrameId != _replacementCompletedFrameId)
            {
                throw new InvalidOperationException(
                    "P3 initial replacement observed an unexpected result classification.");
            }
            StartRecoveryAfterClassification();
            return;
        }
        if (classification.Sequence == _classificationSequenceBaseline)
        {
            return;
        }
        var expectedFailure = _failureRecoveryRequested
            ? AlsP3aResultFailure.Stale
            : AlsP3aResultFailure.GenerationMismatch;
        if (classification.Sequence != _classificationSequenceBaseline + 1 ||
            classification.Identity != _expectedClassificationIdentity ||
            classification.Failure != expectedFailure)
        {
            throw new InvalidOperationException(
                "P3 slot replacement did not receive its expected local classification.");
        }
        if (_active.RuntimeCommittedFrameId != _replacementCompletedFrameId)
        {
            throw new InvalidOperationException(
                "P3 slot replacement advanced commit while classifying its retired generation.");
        }

        StartRecoveryAfterClassification();
    }

    private void StartRecoveryAfterClassification()
    {
        _active.StartReplacementRecovery();
        var lifecycle = _active.LifecycleDiagnostics;
        if (lifecycle.IsVisible || lifecycle.IsVisualReady)
        {
            throw new InvalidOperationException(
                "P3 replacement revealed stale visual state during generation recovery.");
        }
        _committedFrameAtClassification = _active.RuntimeCommittedFrameId;
        _generationMismatchObserved |= _classificationRequired && !_failureRecoveryRequested;
        _replacementPhase = AlsP3ReplacementPhase.AwaitingRecoveryCommit;
    }

    private void TryCompleteRecovery()
    {
        if (_active.RuntimeCommittedFrameId != _replacementCompletedFrameId + 1)
        {
            return;
        }

        _active.CompleteReplacementRecovery();
        _recoveryCommitted = true;
        _replacementPhase = AlsP3ReplacementPhase.Complete;
        _failureRecoveryRequested = false;
    }

    private void PrepareFailureRecoverySpare()
    {
        if (_spare is not null)
        {
            return;
        }
        var handle = new AlsSlotHandle(
            _active.Handle.CharacterId,
            NextGeneration(_active.Handle.Generation));
        _spare = CreateCharacter(
            handle,
            _active.MovementAnchor.GlobalPosition,
            active: false);
    }

    private void DisposeRuntimeCore(bool freeNodes)
    {
        DisposeCharacter(_active, freeNodes);
        DisposeCharacter(_spare, freeNodes);
        _spare = null;
    }

    private void DeactivateCharactersForDisposal()
    {
        DeactivateCharacterForDisposal(_active);
        DeactivateCharacterForDisposal(_spare);
    }

    private static void DeactivateCharacterForDisposal(AlsP3Character? character)
    {
        if (character is null || !GodotObject.IsInstanceValid(character))
        {
            return;
        }
        if (!character.LifecycleDiagnostics.IsDisposed)
        {
            character.SetSchedulingActive(false);
        }
    }

    private void DisposeCharacter(AlsP3Character? character, bool freeNode)
    {
        if (character is null || !GodotObject.IsInstanceValid(character))
        {
            return;
        }

        var lifecycle = character.LifecycleDiagnostics;
        if (!lifecycle.IsDisposed)
        {
            if (lifecycle.IsActive)
            {
                character.SetSchedulingActive(false);
            }
            character.DisposeRuntime();
        }
        if (freeNode)
        {
            if (character.GetParent() == this)
            {
                RemoveChild(character);
            }
            character.Free();
        }
    }

    private int CountVisibleCharacters()
    {
        var count = 0;
        if (_active is not null && GodotObject.IsInstanceValid(_active) &&
            _active.VisualRootVisibilityObservation.IsVisible)
        {
            count++;
        }
        if (_spare is not null && GodotObject.IsInstanceValid(_spare) &&
            _spare.VisualRootVisibilityObservation.IsVisible)
        {
            count++;
        }
        return count;
    }

    private void ValidateVisibilityInvariants()
    {
        if (!_context.HeadlessOrDebug)
        {
            return;
        }

        var visibleCount = CountVisibleCharacters();
        if (visibleCount > 1)
        {
            throw new InvalidOperationException(
                "P3 slot exposed more than one character visual.");
        }
        ValidateCharacterVisibility(_active);
        ValidateCharacterVisibility(_spare);
        if (visibleCount == 0 && _replacementPhase is
            AlsP3ReplacementPhase.AwaitingGenerationMismatch or
            AlsP3ReplacementPhase.AwaitingRecoveryCommit)
        {
            return;
        }
    }

    private static void ValidateCharacterVisibility(AlsP3Character? character)
    {
        if (character is null || !GodotObject.IsInstanceValid(character))
        {
            return;
        }

        var lifecycle = character.LifecycleDiagnostics;
        var visibility = character.VisualRootVisibilityObservation;
        if (visibility.IsVisible &&
            (!lifecycle.IsVisible || !lifecycle.IsActive || !lifecycle.IsVisualReady))
        {
            throw new InvalidOperationException(
                "P3 real visual root remained visible without an active ready character.");
        }
        if (lifecycle.IsVisible && !lifecycle.IsActive)
        {
            throw new InvalidOperationException(
                "P3 slot exposed an inactive character visual.");
        }
        if (lifecycle.IsVisible && !lifecycle.IsVisualReady)
        {
            throw new InvalidOperationException(
                "P3 slot exposed a character visual without committed readiness.");
        }
    }

    private void EnsureConfigured()
    {
        if (!_configured)
        {
            throw new InvalidOperationException("P3 character slot must be configured first.");
        }
    }

    private static uint NextGeneration(uint generation) =>
        generation == uint.MaxValue ? 1 : generation + 1;

    private static void EnsureMainThread()
    {
        if (!GodotThread.IsMainThread())
        {
            throw new InvalidOperationException(
                "P3 character slot lifecycle is restricted to Godot's main thread.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AlsP3CharacterSlot));
        }
    }

}

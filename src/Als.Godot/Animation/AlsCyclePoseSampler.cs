using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

internal sealed class AlsCyclePoseSampler : IDisposable, IAlsStandingDirectionPoseSource
{
    private readonly AnimationPlayer _player;
    private readonly AnimationLibrary _library = new();
    private readonly Godot.Animation _pose = new() { Length = 1, LoopMode = Godot.Animation.LoopModeEnum.Linear };
    private readonly StringName _libraryName = new("als_cycle");
    private readonly StringName _poseName = new("pose");
    private readonly AlsMovementAnimationSource[] _clips;
    private readonly AlsSprintInputSource _sprintInput;
    private readonly AlsLocalPose[] _rest;
    private readonly AlsLocalPose[] _sampled;
    private readonly AlsLocalPose[] _scratch;
    private readonly AlsLocalPose[] _output;
    private readonly AlsLocalPose[] _committed;
    private readonly AlsPrecisePose[] _preciseRest,_preciseSampled,_preciseOutput,_committedPrecise;
    private readonly bool[] _profileBones;
    private readonly int[] _directionInputs;
    private readonly AlsStandingDirectionPoseCache? _directionCache;
    private readonly float[] _curveLengths;
    private readonly string[] _curveNames;
    private readonly Dictionary<string, int> _curveIndices;
    private readonly int[] _godotToLogical;
    private readonly AlsInertialCurve[] _idleCurves, _curveOutput, _committedCurves;
    private bool _hasCurves, _committedHasCurves;
    private readonly int[] _sampleOrder = new int[4];
    private AlsCycleClipTimes _times;
    private System.Numerics.Vector4 _cornerWeights;
    private AlsStandingBlendInputs _sourceInputs;
    private byte _sourceHasSamples;
    private int _sampleCount;
    private bool _registered;
    private bool _disposed;

    public AlsCyclePoseSampler(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set, int[] animationIds, bool[] profileBones,
        AlsStandingDirectionCacheProfile directionProfile,
        Dictionary<string, int>[] curveIds, float[] curveLengths, AlsSprintInputSource sprintInput)
    {
        _player = library.Player;
        _sprintInput=sprintInput;
        _profileBones = profileBones;
        _directionInputs = directionProfile.DirectionInputs.ToArray();
        if (curveIds.Length != animationIds.Length || curveLengths.Length != animationIds.Length)
            throw new ArgumentException("Cycle curve source layout differs.");
        _curveLengths = curveLengths;
        _curveNames = curveIds.SelectMany(ids => ids.Keys).Concat(sprintInput.CurveNames).Append("YawOffset").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        _curveIndices = _curveNames.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index, StringComparer.Ordinal);
        _idleCurves = new AlsInertialCurve[_curveNames.Length]; _curveOutput = new AlsInertialCurve[_curveNames.Length];
        _committedCurves = new AlsInertialCurve[_curveNames.Length];
        var poseSources = library.MovementSources(set, set.Animations[animationIds[0]].SkeletonId);
        var count = poseSources.BoneCount; _godotToLogical = poseSources.GodotToLogical;
        _rest = poseSources.ReferencePose.ToArray();
        _preciseRest=poseSources.PreciseReferencePose.ToArray();
        _preciseSampled=new AlsPrecisePose[count*AlsStandingCyclePose.ClipCount];
        _preciseOutput=new AlsPrecisePose[count]; _committedPrecise=new AlsPrecisePose[count];
        _sampled = new AlsLocalPose[count * AlsStandingCyclePose.ClipCount];
        _scratch = new AlsLocalPose[count * AlsStandingCyclePose.ScratchPoseCount];
        _output = new AlsLocalPose[count];
        _committed = new AlsLocalPose[count];
        _clips = new AlsMovementAnimationSource[animationIds.Length];
        try
        {
            if (animationIds.Length != AlsStandingCyclePose.ClipCount || profileBones.Length != count ||
                _player.HasAnimationLibrary(_libraryName)) throw new InvalidOperationException("Invalid or duplicate cycle pose sampler.");
            for (var bone = 0; bone < _godotToLogical.Length; bone++)
            {
                var rest = library.Skeleton.GetBoneRest(bone);
                using var path = AlsAnimationLibraryBuilder.CreateBonePath(library.Root, library.Skeleton, bone);
                var position = _pose.AddTrack(Godot.Animation.TrackType.Position3D);
                _pose.TrackSetPath(position, path);
                _pose.PositionTrackInsertKey(position, 0, rest.Origin);
                var rotation = _pose.AddTrack(Godot.Animation.TrackType.Rotation3D);
                _pose.TrackSetPath(rotation, path);
                _pose.RotationTrackInsertKey(rotation, 0, rest.Basis.Orthonormalized().GetRotationQuaternion());
                var scale = _pose.AddTrack(Godot.Animation.TrackType.Scale3D);
                _pose.TrackSetPath(scale, path);
                _pose.ScaleTrackInsertKey(scale, 0, rest.Basis.Scale);
            }
            for (var i = 0; i < _clips.Length; i++)
            {
                _clips[i] = poseSources.Create(animationIds[i]);
                SampleClip(i, 0);
            }
            _sampled.AsSpan(0, count).CopyTo(_output);
            _preciseSampled.AsSpan(0,count).CopyTo(_preciseOutput);
            _directionCache = new(directionProfile, _preciseRest, _curveNames.Length, _curveIndices["YawOffset"]);
            Commit();
            WriteOutput();
            if (_library.AddAnimation(_poseName, _pose) != Error.Ok || _player.AddAnimationLibrary(_libraryName, _library) != Error.Ok)
                throw new InvalidOperationException("Cannot register cycle pose output.");
            _registered = true;
            // Topology and key times are immutable. Value-only updates must not enqueue mixer cache rebuilds.
            _pose.SetBlockSignals(true);
        }
        catch { Dispose(); throw; }
    }

    internal ReadOnlySpan<AlsLocalPose> Output => _output;
    internal ReadOnlySpan<AlsPrecisePose> PreciseOutput => _preciseOutput;
    internal ReadOnlySpan<string> CurveNames => _curveNames;
    internal ReadOnlySpan<AlsInertialCurve> CurveOutput => _hasCurves ? _curveOutput :
        throw new InvalidOperationException("Cycle source curves have not been evaluated.");

    internal bool CopyCurves(ReadOnlySpan<string> names, Span<AlsInertialCurve> output)
    {
        if (output.Length != names.Length) throw new ArgumentException("Cycle curve output layout differs.");
        if (!_hasCurves) return false;
        for (var i = 0; i < names.Length; i++) output[i] = _curveIndices.TryGetValue(names[i], out var index) ? _curveOutput[index] : default;
        return true;
    }

    internal int DirectionCacheEvaluations => _directionCache?.Evaluations ?? 0;
    internal int DirectionInputSamples => _directionCache?.InputSamples ?? 0;
    internal int DirectionInputMask => _directionCache?.InputRoleMask ?? 0;
    internal int DirectionCacheInitializations => _directionCache?.Initializations ?? 0;
    public void Commit()
    {
        _output.AsSpan().CopyTo(_committed);
        _preciseOutput.AsSpan().CopyTo(_committedPrecise);
        _curveOutput.AsSpan().CopyTo(_committedCurves); _committedHasCurves = _hasCurves;
        if (_directionCache?.IsPrepared == true) _directionCache.Commit();
    }

    public void Restore()
    {
        _directionCache?.Discard();
        _committed.AsSpan().CopyTo(_output);
        _committedPrecise.AsSpan().CopyTo(_preciseOutput);
        _committedCurves.AsSpan().CopyTo(_curveOutput); _hasCurves = _committedHasCurves;
        WriteOutput();
    }

    public void Apply(in AlsStandingCycleFrame frame)
    {
        Sample(frame);
        WriteOutput();
    }

    internal void SetOutput(ReadOnlySpan<AlsLocalPose> pose)
    {
        if (pose.Length != _output.Length) throw new ArgumentException("Cycle output layout differs.");
        pose.CopyTo(_output);
        WriteOutput();
    }

    internal void SampleIdle(in AlsStandingCycleFrame frame, Span<AlsPrecisePose> output) => _clips[0].SamplePrecise(Seconds(frame, 0), output);

    internal void SampleIdle(in AlsStandingCycleFrame frame, Span<AlsLocalPose> output) => _clips[0].Sample(_rest, Seconds(frame, 0), output);

    internal void Sample(in AlsStandingCycleFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _hasCurves = false;
        _sprintInput.PreparePose(frame);
        _directionCache!.Discard();
        SampleClip(0, Seconds(frame, 0));
        for (var curve = 0; curve < _curveNames.Length; curve++) _idleCurves[curve] = SampleCurve(0, Seconds(frame, 0), curve);
        if (frame.State.MovingWeight == 0)
        {
            _directionCache!.Discard();
            _sampled.AsSpan(0, _rest.Length).CopyTo(_output);
            _preciseSampled.AsSpan(0,_rest.Length).CopyTo(_preciseOutput);
            _idleCurves.CopyTo(_curveOutput.AsSpan()); _hasCurves = true;
        }
        else if (frame.Lifetime.HasInitialized)
        {
            for (var clip = 0; clip < _clips.Length; clip++) _times[clip] = Seconds(frame, clip);
            _sourceHasSamples = 0;
            for(var direction = 0; direction < 6; direction++)
            {
                _sourceInputs[direction] = frame.SourceFilters[direction].Output;
                if(frame.SourceFilters[direction].HasSamples) _sourceHasSamples |= (byte)(1 << direction);
            }
            _directionCache!.Prepare(frame, this, _preciseSampled.AsSpan(0, _rest.Length), _profileBones, _preciseOutput,
                _idleCurves, _curveOutput,
                frame.Lifetime.SprintInitialization, new(0, 0), new(0, (ulong)frame.Movement.Identity.FrameId));
            for(var b=0;b<_output.Length;b++)_output[b]=_preciseOutput[b].ToSingle();
            _hasCurves = true;
        }
        else
        {
            throw new InvalidOperationException("Standing Cycle pose evaluated before initialization.");
        }
    }

    internal void SampleDirect(in AlsStandingCycleFrame frame, Span<AlsLocalPose> output)
    {
        SampleClip(0, Seconds(frame, 0));
        if (frame.State.MovingWeight == 0) _sampled.AsSpan(0, _rest.Length).CopyTo(output);
        else
        {
            // Walk/Run Pose corners are time-bearing BlendSpace samples too; only outer Idle is fixed.
            for (var i = 1; i < 26; i++) SampleClip(i, Seconds(frame, i));
            _sprintInput.PreparePose(frame); _sprintInput.Evaluate(_sampled.AsSpan(25*_rest.Length,_rest.Length));
            Span<int> order = stackalloc int[4];
            for(var direction = 0; direction < 6; direction++)
            {
                var weights = default(System.Numerics.Vector4);
                var count = frame.SourceFilters[direction].HasSamples
                    ? AlsWalkRunBlendSpace.SampleEvaluation(frame.SourceFilters[direction].Output, order, out weights) : 0;
                for(var bone = 0; bone < output.Length; bone++)
                {
                    var pose = count == 0 ? _rest[bone] : AlsPoseBlender.Scale(
                        _sampled[(1 + direction * 4 + order[0]) * output.Length + bone], weights[order[0]]);
                    for(var i = 1; i < count; i++) pose = AlsPoseBlender.Accumulate(pose,
                        _sampled[(1 + direction * 4 + order[i]) * output.Length + bone], weights[order[i]]);
                    _scratch[direction * output.Length + bone] = AlsPoseBlender.Normalize(pose);
                }
            }
            var sprint = AlsStandingSprint.Weights(frame.SprintBlend, frame.SprintMask).PoseSprint;
            for(var bone = 0; bone < output.Length; bone++)
                _scratch[bone] = AlsPoseBlender.Blend(_scratch[bone], _sampled[25 * output.Length + bone], sprint);
            var directionInputs = frame.DirectionInputs;
            AlsStandingCyclePose.ComposeDirections(_scratch.AsSpan(0, 6 * output.Length), _sampled.AsSpan(0, output.Length),
                _profileBones, frame.State.VelocityBlend, frame.Transitions, frame.State.MovingWeight,
                _scratch.AsSpan(6 * output.Length), output, _directionInputs, directionInputs.Cached, _rest);
        }
    }

    void IAlsStandingDirectionPoseSource.SampleInput(int role, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
    {
        if (role == 7)
        {
            _sprintInput.EvaluatePrecise(output);
            for (var curve = 0; curve < curves.Length; curve++) curves[curve] = _sprintInput.Curve(_curveNames[curve]);
            return;
        }
        var direction = role == 6 ? 0 : role;
        _sampleCount = (_sourceHasSamples & (1 << direction)) != 0
            ? AlsWalkRunBlendSpace.SampleEvaluation(_sourceInputs[direction], _sampleOrder, out _cornerWeights) : 0;
        if (_sampleCount == 0)
        {
            _preciseRest.CopyTo(output); curves.Clear(); return; // Outer BlendSpace Initialize clears its sample cache.
        }
        var offset = 1 + direction * 4;
        for (var i = 0; i < _sampleCount; i++) SampleClip(offset + _sampleOrder[i], _times[offset + _sampleOrder[i]]);
        for (var bone = 0; bone < output.Length; bone++)
        {
            var pose = AlsPrecisePoseBlender.Scale(_preciseSampled[(offset + _sampleOrder[0]) * _rest.Length + bone], _cornerWeights[_sampleOrder[0]]);
            for (var i = 1; i < _sampleCount; i++)
                pose = AlsPrecisePoseBlender.Accumulate(pose, _preciseSampled[(offset + _sampleOrder[i]) * _rest.Length + bone], _cornerWeights[_sampleOrder[i]]);
            output[bone] = pose.Normalized();
        }
        for (var curve = 0; curve < curves.Length; curve++)
        {
            var clip = offset + _sampleOrder[0];
            var value = AlsStandingCycleCurves.Scale(SampleCurve(clip, _times[clip], curve), _cornerWeights[_sampleOrder[0]]);
            for (var i = 1; i < _sampleCount; i++)
            {
                clip = offset + _sampleOrder[i];
                value = AlsStandingCycleCurves.Accumulate(value, SampleCurve(clip, _times[clip], curve), _cornerWeights[_sampleOrder[i]]);
            }
            curves[curve] = value;
        }
    }

    internal AlsInertialCurve SourceCurve(in AlsStandingCycleFrame frame, int clip, string name) =>
        clip==25 ? _sprintInput.Curve(frame,name) : _clips[clip].Curve(Seconds(frame, clip), name);

    private float Seconds(in AlsStandingCycleFrame frame, int clip) =>
        frame.HasSourceSeconds ? frame.SourceSeconds[clip] : frame.Times[clip] * _curveLengths[clip];

    private AlsInertialCurve SampleCurve(int clip, float seconds, int curve) =>
        _clips[clip].Curve(seconds, _curveNames[curve]);

    private void SampleClip(int index, float seconds)
    {
        var precise=_preciseSampled.AsSpan(index*_rest.Length,_rest.Length);
        _clips[index].SamplePrecise(seconds,precise);
        for(var b=0;b<_rest.Length;b++)_sampled[index*_rest.Length+b]=precise[b].ToSingle();
    }

    private void WriteOutput()
    {
        for (var bone = 0; bone < _godotToLogical.Length; bone++)
        {
            var pose = _output[_godotToLogical[bone]];
            if (!float.IsFinite(pose.Position.LengthSquared()) || !float.IsFinite(pose.Rotation.LengthSquared()) ||
                !float.IsFinite(pose.Scale.LengthSquared()) || pose.Rotation.LengthSquared() < 1e-8f)
                throw new InvalidOperationException("Non-finite cycle pose.");
            _pose.TrackSetKeyValue(bone * 3, 0, ToGodot(pose.Position));
            _pose.TrackSetKeyValue(bone * 3 + 1, 0, ToGodot(pose.Rotation));
            _pose.TrackSetKeyValue(bone * 3 + 2, 0, ToGodot(pose.Scale));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered && GodotObject.IsInstanceValid(_player)) _player.RemoveAnimationLibrary(_libraryName);
        foreach (var clip in _clips) clip?.Dispose();
        _pose.Dispose();
        _library.Dispose();
        _libraryName.Dispose();
        _poseName.Dispose();
    }

    private static AlsLocalPose Convert(Vector3 position, Quaternion rotation, Vector3 scale) => new(
        new NVector3(position.X, position.Y, position.Z),
        new NQuaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), new NVector3(scale.X, scale.Y, scale.Z));
    private static Vector3 ToGodot(NVector3 value) => new(value.X, value.Y, value.Z);
    private static Quaternion ToGodot(NQuaternion value) => new(value.X, value.Y, value.Z, value.W);

}

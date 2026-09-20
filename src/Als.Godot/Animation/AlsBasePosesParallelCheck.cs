using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal static class AlsBasePosesParallelCheck
{
    private const int CharacterCount = 10;
    private const int FrameCount = 120;

    public static void Run(AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set,
        AlsAnimationLibraryBuildResult library, string[] curveNames)
    {
        var mainThread = Environment.CurrentManagedThreadId;
        // Both runs use the same immutable key-table objects. Everything writable belongs
        // to one character/run. Constructors read Skeleton3D and must remain on this thread.
        var serial = new Owner[CharacterCount];
        var parallel = new Owner[CharacterCount];
        for (var character = 0; character < CharacterCount; character++)
        {
            serial[character] = new(character, definition, set, library, curveNames);
            parallel[character] = new(character, definition, set, library, curveNames);
        }

        var expected = new Result[CharacterCount];
        for (var character = 0; character < CharacterCount; character++) expected[character] = serial[character].Run();

        // A synchronous starting barrier proves that all ten Task.Run jobs are alive on
        // distinct worker threads; a merely queued set of tiny tasks could run serially.
        using var start = new Barrier(CharacterCount);
        var tasks = new Task<Result>[CharacterCount];
        for (var character = 0; character < CharacterCount; character++)
        {
            var owner = parallel[character];
            tasks[character] = Task.Run(() =>
            {
                Require(Environment.CurrentManagedThreadId != mainThread, "BasePoses parallel run reached the engine thread.");
                Require(start.SignalAndWait(TimeSpan.FromSeconds(30)), "BasePoses parallel workers could not start together.");
                return owner.Run();
            });
        }
        var actual = Task.WhenAll(tasks).GetAwaiter().GetResult();
        Require(actual.Select(result => result.ThreadId).Distinct().Count() == CharacterCount,
            "BasePoses parallel run did not use ten distinct worker threads.");

        for (var character = 0; character < CharacterCount; character++)
        {
            var left = expected[character]; var right = actual[character];
            Require(left.ThreadId == mainThread, "BasePoses serial baseline left the engine thread.");
            for (var frame = 0; frame < FrameCount; frame++)
                Require(left.PrefixDigests[frame].AsSpan().SequenceEqual(right.PrefixDigests[frame]),
                    $"BasePoses character {character}, frame {frame + 1} differs between serial and parallel TRS/curve trajectories.");
            Require(left.State == right.State,
                $"BasePoses character {character} retained different serial/parallel evaluator history.");
        }

        // Logging is deliberately after the worker join. No Godot API runs on a worker.
        Godot.GD.Print($"BASE_POSES_PARALLEL_OK characters={CharacterCount} frames_per_character={FrameCount} " +
            $"serial_frames={CharacterCount * FrameCount} parallel_frames={CharacterCount * FrameCount} worker_threads={CharacterCount} " +
            "rates=30/60/120 shared_key_tables=2 digest=sha256_trs_bits_curve_presence state=identical");
    }

    private sealed class Owner
    {
        private readonly int _characterIndex, _rate;
        private readonly uint _characterId;
        private readonly AlsBasePosesSourceSampler _sampler;
        private readonly AlsBasePosesRuntime _runtime;
        private readonly AlsLocalPose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private readonly byte[] _frameBytes;

        public Owner(int characterIndex, AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set,
            AlsAnimationLibraryBuildResult library, string[] curveNames)
        {
            _characterIndex = characterIndex; _characterId = (uint)(101 + characterIndex);
            _rate = (characterIndex % 3) switch { 0 => 30, 1 => 60, _ => 120 };
            _sampler = new(definition.BasePoses, set, library, curveNames,
                definition.BasePoseRetarget, definition.BasePoseSourceKeys);
            _runtime = new(definition.BasePoses, _sampler.Layout.ReferencePose, curveNames);
            _pose = new AlsLocalPose[_sampler.Layout.ReferencePose.Length];
            _curves = new AlsInertialCurve[curveNames.Length];
            // Frame, character, rate, followed by ten binary32 TRS components per bone,
            // and the binary32 value plus explicit presence integer for each curve.
            _frameBytes = new byte[checked(12 + _pose.Length * 40 + _curves.Length * 8)];
        }

        // All fields touched here are managed snapshots. No Skeleton3D, AnimationPlayer,
        // scene-tree or other engine API is consulted after construction.
        public Result Run()
        {
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var prefixes = new byte[FrameCount][];
            for (var frame = 1; frame <= FrameCount; frame++)
            {
                var identity = new AlsFrameIdentity(frame, _characterId, 7);
                var phase = (frame - 1 + _characterIndex) % 4;
                var input = default(AlsLayeringInput) with
                {
                    Identity = identity,
                    BasePoseNormal = phase switch { 0 => 1, 2 => .25, _ => 0 },
                    BasePoseCrouching = phase switch { 1 => 1, 2 => .75, _ => 0 },
                };
                var context = new AlsPoseUpdateContext(identity, frame % 17 == 0 ? 0 : 1, 1f / _rate);
                _runtime.BeginCandidate(identity, _sampler);
                if (frame == 1)
                {
                    _runtime.Initialize(new(0, 1));
                    _runtime.CacheBones(new(0, 1));
                }
                _runtime.Update(context, input);
                _runtime.Evaluate(_pose, _curves);
                AppendFrame(digest, frame);
                prefixes[frame - 1] = digest.GetCurrentHash();
                _runtime.Commit();
            }
            Require(!_runtime.HasCandidate && _runtime.State.Identity == new AlsFrameIdentity(FrameCount, _characterId, 7),
                "BasePoses character left an uncommitted or foreign final candidate.");
            return new(prefixes, _runtime.State, Environment.CurrentManagedThreadId);
        }

        private void AppendFrame(IncrementalHash digest, int frame)
        {
            var offset = 0;
            WriteInt(ref offset, frame); WriteInt(ref offset, (int)_characterId); WriteInt(ref offset, _rate);
            foreach (var pose in _pose)
            {
                WriteFloat(ref offset, pose.Position.X); WriteFloat(ref offset, pose.Position.Y); WriteFloat(ref offset, pose.Position.Z);
                WriteFloat(ref offset, pose.Rotation.X); WriteFloat(ref offset, pose.Rotation.Y);
                WriteFloat(ref offset, pose.Rotation.Z); WriteFloat(ref offset, pose.Rotation.W);
                WriteFloat(ref offset, pose.Scale.X); WriteFloat(ref offset, pose.Scale.Y); WriteFloat(ref offset, pose.Scale.Z);
            }
            foreach (var curve in _curves)
            {
                Require(!curve.Present, "BasePoses parallel owner fabricated a source curve.");
                WriteFloat(ref offset, curve.Value); WriteInt(ref offset, curve.Present ? 1 : 0);
            }
            Require(offset == _frameBytes.Length, "BasePoses trajectory digest omitted output components.");
            digest.AppendData(_frameBytes);
        }

        private void WriteFloat(ref int offset, float value) => WriteInt(ref offset, BitConverter.SingleToInt32Bits(value));
        private void WriteInt(ref int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_frameBytes.AsSpan(offset, sizeof(int)), value);
            offset += sizeof(int);
        }
    }

    private sealed record Result(byte[][] PrefixDigests, AlsBasePosesState State, int ThreadId);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

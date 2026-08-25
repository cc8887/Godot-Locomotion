using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

public sealed class AlsAnimationEventDigestTests
{
    [Fact]
    public void ProducesTheSameDigestForEquivalentTimeWindows()
    {
        var clip = CreateClip();
        var splitDigest = AlsAnimationEventDigest.OffsetBasis;
        var splitCount = AlsAnimationEventDigest.Advance(clip, 0.0, 0.5, ref splitDigest);
        splitCount += AlsAnimationEventDigest.Advance(clip, 0.5, 0.7, ref splitDigest);

        var wholeDigest = AlsAnimationEventDigest.OffsetBasis;
        var wholeCount = AlsAnimationEventDigest.Advance(clip, 0.0, 0.7, ref wholeDigest);

        Assert.Equal(4, splitCount);
        Assert.Equal(splitCount, wholeCount);
        Assert.Equal(splitDigest, wholeDigest);
    }

    [Fact]
    public void DoesNotRepeatEventsPastTheEndOfANonLoopingClip()
    {
        var clip = CreateClip();
        var digest = AlsAnimationEventDigest.OffsetBasis;

        var firstCount = AlsAnimationEventDigest.Advance(clip, 0.0, 2.0, ref digest);
        var digestAtEnd = digest;
        var secondCount = AlsAnimationEventDigest.Advance(clip, 2.0, 3.0, ref digest);

        Assert.Equal(4, firstCount);
        Assert.Equal(0, secondCount);
        Assert.Equal(digestAtEnd, digest);
    }

    private static AlsAnimationDefinition CreateClip()
    {
        var manifest = AlsManifestSerializer.Load(AlsManifestSerializerTests.FixturePath());
        var clip = AlsAnimationSetCompiler.Compile(manifest).Animations[0];
        return clip with
        {
            PlayLength = 1.0f,
            Loop = false,
            Notifies =
            [
                new AlsAnimationNotifyDefinition("Footstep", 0.1f, 0f, 0),
                new AlsAnimationNotifyDefinition("Footstep", 0.6f, 0f, 1),
            ],
            SyncMarkers =
            [
                new AlsAnimationSyncMarkerDefinition("Left", 0.1f),
                new AlsAnimationSyncMarkerDefinition("Right", 0.6f),
            ],
        };
    }
}

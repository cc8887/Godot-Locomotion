using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredV4SourceCurveTests
{
    private const string Asset = "/Game/AdvancedLocomotionV4/CharacterAssets/Test.Test";
    private static AlsFloatCurveDefinition Curve(int id, string name) =>
        new(id, AlsCanonicalCurveKind.None, name, AlsCurveProvenance.SourceCurve, []);

    [Fact]
    public void SourceBindingKeepsMissingDifferentSideAndGraphOnlyInputsAbsent()
    {
        string[] names = ["GroundPredictionBlock", "FootLeftLock", "FootRightLock", "FootLeftIk", "FootRightIk", "FootLock_R"];
        int[] ids = [-1, -1, -1, -1, -1, 17];
        AlsRefactoredV4SourceCurves.Bind(Asset, [Curve(4, "mask_landprediction"), Curve(17, "FootLock_R")], names, ids);
        Assert.Equal(new[] { 4, -1, 17, -1, -1, 17 }, ids);
    }

    [Fact]
    public void NativeRefactoredCurveCannotBeSilentlyOverwrittenByV4Alias()
    {
        string[] names = ["FootLeftLock"];
        int[] ids = [9];
        Assert.Throws<ArgumentException>(() => AlsRefactoredV4SourceCurves.Bind(Asset,
            [Curve(9, "FootLeftLock"), Curve(7, "FootLock_L")], names, ids));
        Assert.Equal(9, ids[0]);
    }

    [Fact]
    public void DuplicateAuthoredSourceIsRejectedInsteadOfChoosingOne()
    {
        string[] names = ["FootLeftLock"];
        int[] ids = [-1];
        Assert.Throws<ArgumentException>(() => AlsRefactoredV4SourceCurves.Bind(Asset,
            [Curve(4, "FootLock_L"), Curve(7, "footlock_l")], names, ids));
    }

    [Theory]
    [InlineData("/Game/ALS/Character/Test.Test")]
    [InlineData("/Game/AdvancedLocomotionV4_Other/Test.Test")]
    public void UnverifiedAssetNamespaceCannotOptIntoVersionAdaptation(string asset)
    {
        string[] names = ["GroundPredictionBlock"];
        int[] ids = [-1];
        Assert.Throws<ArgumentException>(() => AlsRefactoredV4SourceCurves.Bind(asset,
            [Curve(4, "Mask_LandPrediction")], names, ids));
    }

    [Fact]
    public void ExistingLayoutsDoNotEnableVersionAdaptation()
    {
        string[] names = ["Mask_LandPrediction", "FootLock_L"];
        int[] ids = [4, 7];
        AlsRefactoredV4SourceCurves.Bind("/Game/Unrelated/Test.Test",
            [Curve(4, "Mask_LandPrediction"), Curve(7, "FootLock_L")], names, ids);
        Assert.Equal(new[] { 4, 7 }, ids);
    }
}

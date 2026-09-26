using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredAirSettings
{
    public const string LeanPath="/ALS/ALS/Data/AnimationInstance/Air/CF_Als_LeanAmount_Air.CF_Als_LeanAmount_Air";
    public string CatalogDigest {get;}
    public float LeanHalfLife {get;}
    public AlsRefactoredMovementCurve Lean {get;}
    public AlsRefactoredAirSettings(string json,AlsRefactoredAnimationCatalog catalog,AlsRefactoredMovementSettings movement)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        Expect(root,new{schemaVersion=1,source=AlsRefactoredMovementSettings.Source,animationClass="/ALS/ALS/Character/AB_Als.AB_Als_C"});
        CatalogDigest=catalog.IndexDigest;
        if(!string.Equals(root.GetProperty("catalogSha256").GetString(),CatalogDigest,StringComparison.OrdinalIgnoreCase)||movement.CatalogDigest!=CatalogDigest)
            throw new ArgumentException("Foreign air settings catalog.");
        LeanHalfLife=root.GetProperty("leanHalfLife").GetSingle();
        if(LeanHalfLife!=movement.LeanHalfLife)throw new ArgumentException("Air and Grounded must share Lean settings.");
        var row=root.GetProperty("leanCurve");Expect(row,new{path=LeanPath});
        Expect(catalog.Read(LeanPath),new{@class="CurveFloat"});
        if(!catalog.Read(AlsRefactoredMovementSettings.Source).GetProperty("nativeText").GetString()!.Contains(
            "LeanAmountCurve=\"/Script/Engine.CurveFloat'"+LeanPath+"'\"",StringComparison.Ordinal))
            throw new ArgumentException("Original air Lean binding differs.");
        Lean=new(row);
    }
}

public readonly record struct AlsRefactoredInAirState(bool Jumped,float JumpRate,float VerticalVelocity,float Prediction)
{
    public static AlsRefactoredInAirState Initial=>new(false,1,0,1);
}

public sealed partial class AlsRefactoredMovementParentRuntime
{
    private AlsRefactoredAirSettings? _airSettings;
    private bool _airPrepared,_airGameWorld;
    private float _airPrediction;
    private AlsRefactoredInAirState _airCandidate;
    public AlsRefactoredInAirState CommittedAir {get;private set;}=AlsRefactoredInAirState.Initial;
    public AlsRefactoredInAirState AirCandidate {get{Check(_identity.FrameId);return _airCandidate;}}

    public void PrepareInAir(long frame,AlsRefactoredAirSettings settings,bool jumpRequested,float prediction,bool gameWorld=true)
    {
        Check(frame);RequireInput();
        if(_airPrepared||settings.CatalogDigest!=_digest||!float.IsFinite(prediction))throw new ArgumentException("Foreign/repeated InAir Parent input.");
        _airSettings=settings;_airPrediction=prediction;_airGameWorld=gameWorld;_airPrepared=true;
        _airCandidate=_airCandidate with{Jumped=!_input.PendingUpdate&&(_airCandidate.Jumped||jumpRequested)};
    }
    public void RefreshInAir(long frame)
    {
        Check(frame);RequireInput();if(!_airPrepared)throw new InvalidOperationException("No InAir Parent input.");
        if(!_airGameWorld)return;
        try
        {
            var state=_airCandidate;
            if(state.Jumped)state=state with{Jumped=false,JumpRate=1.2f+(1.5f-1.2f)*Math.Clamp(_input.Speed*(1f/600),0,1)};
            state=state with{VerticalVelocity=(float)_input.Velocity.Z,Prediction=_airPrediction};
            _movementCandidate=AlsRefactoredMovementModel.RefreshInAir(_movementCandidate,_input,
                _airSettings!.Lean.Sample(state.VerticalVelocity),_airSettings.LeanHalfLife);
            ValidateMovement();_airCandidate=state;
        }
        catch{_faulted=true;throw;}
    }
}

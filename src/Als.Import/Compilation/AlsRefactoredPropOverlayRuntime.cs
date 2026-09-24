using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredPropOverlayInput(float Walking, float Sprinting, float Standing, float Crouching, float Pitch);

public sealed class AlsRefactoredPropOverlayRuntime
{
    private readonly AlsRefactoredPropOverlayProfile _profile;
    private readonly AlsRefactoredPropOverlayUpdateRuntime _update;
    private readonly int _player;
    private readonly AlsOverlayActionMix _actionMix = new(), _aimMix = new();
    private readonly AlsRefactoredAdditiveSource.Sampler[] _aim;
    private readonly AlsPrecisePose[] _pose, _standingAim, _crouchingAim, _additive;
    private readonly AlsQuaternion[] _meshScratch;
    private readonly AlsInertialCurve[] _curves, _standingCurves, _crouchingCurves;
    private readonly AlsInertialCurve[][] _aimCurves;
    private AlsRefactoredPropOverlayInput _input;
    private bool _evaluated, _faulted;
    private AlsRefactoredSourcePlayerRuntime? _sampledSource;
    public AlsPropOverlayUpdateState CommittedState => _update.CommittedState;
    public AlsPropOverlayUpdateResult Candidate => _update.Candidate;
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _update.SourceInputs;
    public ReadOnlySpan<AlsPrecisePose> Pose => _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Prop pose unavailable.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Prop curves unavailable.");
    internal AlsRefactoredPropOverlayRuntime(AlsRefactoredPropOverlayProfile profile, int player)
    {
        _profile = profile; _player = player; _update = profile.Update.CreateRuntime(player);
        _aim = profile.Aim.Select(a => a.CreateSampler()).ToArray();
        var bones = profile.BoneNames.Length; var curves = profile.CurveNames.Length;
        _pose = new AlsPrecisePose[bones]; _standingAim = new AlsPrecisePose[bones]; _crouchingAim = new AlsPrecisePose[bones]; _additive = new AlsPrecisePose[bones]; _meshScratch = new AlsQuaternion[2*bones];
        _curves = new AlsInertialCurve[curves]; _standingCurves = new AlsInertialCurve[curves]; _crouchingCurves = new AlsInertialCurve[curves];
        _aimCurves = profile.Aim.Select(a => new AlsInertialCurve[a.CurveNames.Length]).ToArray();
    }
    public void Prepare(long frame, AlsOverlayAction action, bool aiming, in AlsRefactoredPropOverlayInput input, float delta, bool reinitialize = false)
    {
        if (!Unit(input.Walking) || !Unit(input.Sprinting) || !Unit(input.Standing) || !Unit(input.Crouching) || !Unit(input.Pitch)) throw new ArgumentException("Invalid prop pose input.");
        _update.Prepare(frame, action, aiming, delta, reinitialize);
        _input = input; _actionMix.Prepare(Candidate.State.Actions.Weights);
        var aim = AlsPropOverlayUpdate.AimWeights(Candidate.State); _aimMix.Prepare(new(aim.X, aim.Y, 0, 0));
        _evaluated = false; _faulted = false; _sampledSource = null;
    }
    public void Evaluate(long frame, AlsRefactoredSourcePlayerRuntime players)
    {
        ValidateCommit(frame);
        try
        {
            players.ValidateCommit(frame);
            if (players.CatalogDigest != _profile.Update.CatalogDigest || players.Source(_player) != AlsRefactoredDefaultOverlayProfile.IdleSource ||
                !players.BoneNames(_player).SequenceEqual(_profile.BoneNames) || !players.CurveNames(_player).SequenceEqual(_profile.IdleNames))
                throw new ArgumentException("Foreign prop player.");
            if (_evaluated)
            { if (!ReferenceEquals(players, _sampledSource)) throw new ArgumentException("Prop source owner changed."); return; }
            var baseActive = Candidate.State.Actions.Weights.X > AlsPoseBlender.WeightThreshold;
            var aimActive = AlsPropOverlayUpdate.AimWeights(Candidate.State).Y > AlsPoseBlender.WeightThreshold;
            if (baseActive)
            {
                players.Evaluate(frame, _player);
                if (aimActive) SampleAim();
            }
            Span<AlsPrecisePose> branches = stackalloc AlsPrecisePose[4];
            Span<AlsPrecisePose> aims = stackalloc AlsPrecisePose[4];
            for (var bone = 0; bone < _pose.Length; bone++)
            {
                var normal = Frame(0,bone); branches.Fill(normal); branches[2] = Frame(4,bone);
                if (baseActive)
                {
                    var walk = _profile.Kind == AlsRefactoredPropOverlayKind.Binoculars ?
                        AlsRefactoredDefaultOverlay.Two(Frame(6,bone),Frame(7,bone),_input.Sprinting) : normal;
                    var basePose = Stance(normal,walk,Frame(4,bone),_profile.Reference[bone],_input.Walking);
                    aims[0]=basePose;
                    aims[1]=aimActive ? Stance(_standingAim[bone],_standingAim[bone],_crouchingAim[bone],_profile.Reference[bone],0) : basePose;
                    branches[0]=AlsPrecisePoseBlender.LocalApply(_aimMix.Pose(aims),players.Pose(_player)[bone],.5f);
                }
                _pose[bone]=_actionMix.Pose(branches);
            }
            Span<AlsInertialCurve> curves = stackalloc AlsInertialCurve[4]; Span<AlsInertialCurve> aimCurves = stackalloc AlsInertialCurve[4];
            for(var c=0;c<_curves.Length;c++)
            {
                var normal=FrameCurve(0,c);curves.Fill(normal);curves[2]=FrameCurve(4,c);
                if(baseActive)
                {
                    var walk=_profile.Kind==AlsRefactoredPropOverlayKind.Binoculars ? AlsRefactoredDefaultOverlay.TwoCurve(FrameCurve(6,c),FrameCurve(7,c),_input.Sprinting):normal;
                    aimCurves[0]=StanceCurve(normal,walk,FrameCurve(4,c),_input.Walking);
                    aimCurves[1]=aimActive?StanceCurve(_standingCurves[c],_standingCurves[c],_crouchingCurves[c],0):aimCurves[0];
                    curves[0]=_aimMix.Curve(aimCurves);var idle=_profile.IdleMap[c];
                    if(idle>=0)curves[0]=AlsStandingCycleCurves.Accumulate(curves[0],players.Curves(_player)[idle],.5f);
                }
                var name=_profile.CurveNames[c];var binoculars=_profile.Kind==AlsRefactoredPropOverlayKind.Binoculars;
                if(name==(binoculars?"LayerArmRight":"LayerArmLeft"))curves[2]=AlsStandingCycleCurves.ModifyBlend(curves[2],3,1);
                if(name=="LayerArmLeft" || binoculars&&name=="LayerArmRight")curves[3]=AlsStandingCycleCurves.ModifyBlend(curves[3],3,1);
                if(!binoculars&&name=="LayerArmLeftAdditive")curves[3]=AlsStandingCycleCurves.ModifyBlend(curves[3],0,1);
                _curves[c]=_actionMix.Curve(curves);
            }
            _sampledSource=players;_evaluated=true;
        }
        catch { _faulted=true;throw; }
    }
    private void SampleAim()
    {
        for(var stance=0;stance<2;stance++)
        {
            var pose=stance==0?_standingAim:_crouchingAim;var curves=stance==0?_standingCurves:_crouchingCurves;
            _aim[stance].Sample(_input.Pitch,_additive,_aimCurves[stance]);
            for(var bone=0;bone<pose.Length;bone++)pose[bone]=stance==0?AlsRefactoredDefaultOverlay.Two(Frame(2,bone),Frame(3,bone),_input.Walking):Frame(5,bone);
            AlsPrecisePoseBlender.MeshApply(pose,_additive,_profile.Parents,_meshScratch,pose,1);
            for(var c=0;c<curves.Length;c++)curves[c]=stance==0?AlsRefactoredDefaultOverlay.TwoCurve(FrameCurve(2,c),FrameCurve(3,c),_input.Walking):FrameCurve(5,c);
            var map=_profile.AimMap[stance];for(var c=0;c<map.Length;c++)curves[map[c]]=AlsStandingCycleCurves.Accumulate(curves[map[c]],_aimCurves[stance][c],1);
        }
    }
    private AlsPrecisePose Frame(int f,int bone)=>_profile.Frames[f*_pose.Length+bone];
    private AlsInertialCurve FrameCurve(int f,int c)=>_profile.FrameCurves[f*_curves.Length+c];
    private AlsPrecisePose Stance(AlsPrecisePose a,AlsPrecisePose b,AlsPrecisePose c,AlsPrecisePose reference,float walking)
    { Span<AlsPrecisePose> poses=stackalloc AlsPrecisePose[]{a,b,c};return AlsRefactoredDefaultOverlay.Bone(poses,reference,new(walking,_input.Standing,_input.Crouching,0,0),default); }
    private AlsInertialCurve StanceCurve(AlsInertialCurve a,AlsInertialCurve b,AlsInertialCurve c,float walking)
    { Span<AlsInertialCurve> curves=stackalloc AlsInertialCurve[]{a,b,c};return AlsRefactoredDefaultOverlay.Curve(curves,new(walking,_input.Standing,_input.Crouching,0,0),default); }
    public void ValidateCommit(long frame){_update.ValidateCommit(frame);if(_faulted)throw new ArgumentException("Prop frame faulted.");}
    public void Commit(long frame){ValidateCommit(frame);_update.Commit(frame);Clear();}
    public void Cancel(){_update.Cancel();Clear();}
    private void Clear(){_faulted=false;_evaluated=false;_sampledSource=null;}
    private static bool Unit(float x)=>float.IsFinite(x)&&x is >=0 and <=1;
}

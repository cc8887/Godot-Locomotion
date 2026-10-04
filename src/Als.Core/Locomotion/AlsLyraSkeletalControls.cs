using System.Collections.Immutable;
namespace GodotAls.Core.Locomotion;

public sealed record AlsLyraSkeletalHistory(AlsFootPlacementHistory Foot,ImmutableArray<AlsLegIkBendHistory> Legs)
{public static AlsLyraSkeletalHistory Default=>new(AlsFootPlacementHistory.Default,[default,default]);}

// One FCSPose for the complete original twelve-node layer. No intermediate
// local exports, skeleton writes, animation clocks or independently committed nodes.
public sealed class AlsLyraSkeletalControls
{
    private readonly AlsComponentPose _pose;
    private AlsComponentCopyBoneController? _copy;
    private AlsLyraRootWeaponControls? _modify;
    private AlsTwoBoneIkController? _right,_left;
    private AlsLyraFootPlacement? _foot;
    private AlsLegIkController? _legs;
    private int _rightFk,_leftFk,_rightIk,_leftIk,_gun;
    private readonly int _count;
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _reference;
    private readonly Func<string,int> _bone;
    private readonly Dictionary<int,ImmutableArray<int>> _bindings=[];
    private static readonly int[] Nodes=[103,102,104,110,109,105,107,106];
    public IReadOnlyDictionary<int,ImmutableArray<int>> BoneBindings=>_bindings;
    public ReadOnlySpan<float> FootLengths=>_foot is null?[]:_foot.LimbLengths;
    private readonly float _fkWeight;
    public AlsLyraSkeletalControls(ReadOnlySpan<int> parents,ReadOnlySpan<AlsPrecisePose> reference,Func<string,int> bone,float fkWeight,
        bool deferBoneCache=false)
    {
        if(!float.IsFinite(fkWeight)||fkWeight is <0 or >1)throw new ArgumentException("Invalid HandFKWeight.");
        _count=parents.Length;_pose=new(parents);_fkWeight=fkWeight;_parents=parents.ToArray();_reference=reference.ToArray();_bone=bone;
        if(!deferBoneCache)foreach(var node in Nodes)CacheBoneReferences(node);
    }
    // The real layer invokes this at each original CacheBones node. Fixed
    // ALS81 topology is immutable; rebuilding controllers retains external
    // foot interpolation and FK-name-owned bend histories.
    public bool CacheBoneReferences(int node)
    {
        ImmutableArray<int> ids;
        switch(node)
        {
            case 103:
                _rightFk=_bone("hand_r");_leftFk=_bone("hand_l");_rightIk=_bone("ik_hand_r");_leftIk=_bone("ik_hand_l");_gun=_bone("ik_hand_gun");
                ids=[_rightFk,_leftFk,_rightIk,_leftIk,_gun];break;
            case 102:
                ids=[_bone("VB IK_Hand_L_weaponSpace"),_bone("ik_hand_l")];_copy=new(_parents,ids[0],ids[1],true,true,false);break;
            case 104:case 106:
                ids=[_bone(node==104?"root":"weapon_r")];_modify??=new(_parents,_bone("root"),_bone("weapon_r"));break;
            case 110:
                ids=[_bone("hand_r"),_bone("ik_hand_r"),_bone("lowerarm_r")];_right=new(_parents,ids[0],ids[1],ids[2],new(0,50,0),false);break;
            case 109:
                ids=[_bone("hand_l"),_bone("ik_hand_l"),_bone("lowerarm_l")];_left=new(_parents,ids[0],ids[1],ids[2],new(0,-50,0),true);break;
            case 105:
                ids=[_bone("pelvis"),_bone("ik_foot_root"),_bone("foot_r"),_bone("ik_foot_r"),_bone("ball_r"),_bone("foot_l"),_bone("ik_foot_l"),_bone("ball_l")];
                _foot=new(_parents,_reference,ids[0],ids[1],ids[2],ids[3],ids[4],ids[5],ids[6],ids[7]);break;
            case 107:
                ids=[_bone("foot_l"),_bone("ik_foot_l"),_bone("foot_r"),_bone("ik_foot_r")];_legs=new(_parents,[new(ids[0],ids[1]),new(ids[2],ids[3])]);break;
            default:return false;
        }
        foreach(var index in ids)if((uint)index>=_count)throw new ArgumentException("Unbound SkeletalControls bone.");
        _bindings[node]=ids;return true;
    }
    public AlsLyraSkeletalHistory Evaluate(ReadOnlySpan<AlsPrecisePose> input,ReadOnlySpan<float> alphas,
        in AlsFootCharacterInput character,IAlsFootGroundQuery ground,AlsLyraSkeletalHistory history,Span<AlsPrecisePose> output)
    {
        if(_bindings.Count!=8)throw new InvalidOperationException("SkeletalControls needs its complete CacheBones traversal.");
        if(input.Length!=_count||output.Length!=_count||input.Overlaps(output)||alphas.Length!=8||history.Legs.Length!=2||
            history.Foot.Legs.Length!=2)throw new ArgumentException("Invalid SkeletalControls buffers/history.");
        foreach(var a in alphas)if(!float.IsFinite(a)||a is <0 or >1)throw new ArgumentException("Invalid SkeletalControls alpha.");
        character.Component.Validate();if(!character.FloorPoint.IsFinite||!character.FloorNormal.IsFinite||!character.Velocity.IsFinite)
            throw new ArgumentException("Invalid SkeletalControls character observation.");
        _pose.Begin(input);
        Retarget(alphas[0]);_copy!.Evaluate(_pose,alphas[1]);_modify!.Root(_pose,alphas[2]);
        _right!.Evaluate(_pose,default,alphas[3]);_left!.Evaluate(_pose,default,alphas[4]);
        var foot=_foot!.Evaluate(_pose,alphas[5],character,ground,history.Foot);
        var legs=_legs!.Evaluate(_pose,alphas[6],history.Legs);_modify.Weapon(_pose,alphas[7]);
        _pose.Export(output);return new(foot,legs);
    }
    private void Retarget(float alpha)
    {
        if(alpha<=AlsPoseBlender.WeightThreshold)return;
        AlsDoubleVector fk,ik;
        if(_fkWeight>=1-AlsPoseBlender.WeightThreshold){fk=_pose.Component(_rightFk).Position;ik=_pose.Component(_rightIk).Position;}
        else if(_fkWeight<=AlsPoseBlender.WeightThreshold){fk=_pose.Component(_leftFk).Position;ik=_pose.Component(_leftIk).Position;}
        else
        {
            var rf=_pose.Component(_rightFk).Position;var ri=_pose.Component(_rightIk).Position;
            var lf=_pose.Component(_leftFk).Position;var li=_pose.Component(_leftIk).Position;
            fk=lf+(rf-lf)*_fkWeight;ik=li+(ri-li)*_fkWeight;
        }
        var offset=fk-ik;if(offset.NearlyZero(1e-4f))return;
        var gun=_pose.Component(_gun);_pose.Apply([_gun],[gun with{Position=gun.Position+offset}],alpha);
    }
}

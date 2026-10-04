namespace GodotAls.Core.Locomotion;

// Original ModifyBone nodes: root additive component translation and weapon_r
// component scale replacement. Standalone calls own their component pose;
// internal calls retain the enclosing SkeletalControls component cache.
public sealed class AlsLyraRootWeaponControls
{
    private readonly AlsComponentPose _components;
    private readonly int _root,_weapon;
    public AlsLyraRootWeaponControls(ReadOnlySpan<int> parents,int root,int weapon)
    {
        for(var bone=0;bone<parents.Length;bone++)
            if(parents[bone]<-1||parents[bone]>=bone)throw new ArgumentException("ModifyBone requires a parent-first hierarchy.");
        if((uint)root>=parents.Length||(uint)weapon>=parents.Length||parents[root]!=-1)
            throw new ArgumentException("Invalid Lyra control bone layout.");
        _components=new(parents);_root=root;_weapon=weapon;
    }
    public void Root(ReadOnlySpan<AlsPrecisePose> input,float alpha,Span<AlsPrecisePose> output)=>Evaluate(input,alpha,output,true);
    public void Weapon(ReadOnlySpan<AlsPrecisePose> input,float alpha,Span<AlsPrecisePose> output)=>Evaluate(input,alpha,output,false);
    private void Evaluate(ReadOnlySpan<AlsPrecisePose> input,float alpha,Span<AlsPrecisePose> output,bool root)
    {
        if(!float.IsFinite(alpha)||alpha is <0 or >1||input.Overlaps(output)||input.Length!=output.Length)
            throw new ArgumentException("Invalid ModifyBone buffers or alpha.");
        _components.Begin(input);Evaluate(_components,alpha,root);_components.Export(output);
    }
    internal void Root(AlsComponentPose pose,float alpha)=>Evaluate(pose,alpha,true);
    internal void Weapon(AlsComponentPose pose,float alpha)=>Evaluate(pose,alpha,false);
    private void Evaluate(AlsComponentPose pose,float alpha,bool root)
    {
        var bone=root?_root:_weapon;
        if(alpha>AlsPoseBlender.WeightThreshold)
        {
            var value=pose.Component(bone);
            value=root?value with{Position=value.Position+new AlsDoubleVector(0,0,-2)}:
                value with{Scale=new AlsDoubleVector(.05,.05,.05)};
            pose.Apply([bone],[value],alpha);
        }
    }
}

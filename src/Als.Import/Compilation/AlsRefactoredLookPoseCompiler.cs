using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredLookPoseSource
{
    private readonly AlsMantlingPoseSource[] _targets;
    private readonly AlsMantlingPoseSource _basis;
    public ReadOnlySpan<string> BoneNames=>_basis.BoneNames;
    public ReadOnlySpan<int> Parents=>_basis.Parents;
    internal AlsRefactoredLookPoseSource(AlsMantlingPoseSource[] targets,AlsMantlingPoseSource basis)
    {_targets=targets;_basis=basis;}
    public Sampler CreateSampler()=>new(this);
    public sealed class Sampler
    {
        private readonly AlsMantlingPoseSource.Sampler[] _targets;
        private readonly AlsPrecisePose[] _basis,_target,_sample;
        private readonly AlsQuaternion[] _scratch;
        private readonly int[] _parents;
        private bool _busy;
        internal Sampler(AlsRefactoredLookPoseSource owner)
        {
            _targets=owner._targets.Select(s=>s.CreateSampler()).ToArray();_parents=owner.Parents.ToArray();
            _basis=new AlsPrecisePose[_parents.Length];_target=new AlsPrecisePose[_parents.Length];_sample=new AlsPrecisePose[_parents.Length];_scratch=new AlsQuaternion[_parents.Length*2];
            owner._basis.CreateSampler().Sample(0,true,false,false,_basis);
        }
        // Sample indices are original BlendSpace order: Forward, Down, Up.
        // This is an explicit evaluator, not a player: no clock, notifies or root motion.
        public void SampleSequence(int sample,float normalizedTime,Span<AlsPrecisePose> output)
        {
            if(_busy||(uint)sample>=3||!float.IsFinite(normalizedTime)||output.Length!=_target.Length)
                throw new ArgumentException("Invalid or reentrant Look sample.");
            _busy=true;
            try
            {
                _targets[sample].Sample(System.Math.Clamp(normalizedTime,0,1),true,false,false,_target);
                AlsPrecisePoseBlender.MeshDifference(_target,_basis,_parents,_scratch,output);
            }
            finally {_busy=false;}
        }
        // Sample selection/order belongs to the BlendSpace evaluator. This method
        // consumes its final normalized weights, without advancing sample time.
        public void SampleBlend(ReadOnlySpan<AlsAimGridVertex> samples,float normalizedTime,Span<AlsPrecisePose> output)
        {
            if(_busy||samples.IsEmpty||samples.Length>3||output.Length!=_sample.Length||!float.IsFinite(normalizedTime))
                throw new ArgumentException("Invalid Look blend layout.");
            var seen=0;var total=0f;
            foreach(var sample in samples)
            {
                if((uint)sample.Sample>=3||(seen&(1<<sample.Sample))!=0||!float.IsFinite(sample.Weight)||
                    sample.Weight<AlsPoseBlender.WeightThreshold||sample.Weight>1)
                    throw new ArgumentException("Invalid Look blend sample.");
                seen|=1<<sample.Sample;total+=sample.Weight;
            }
            if(MathF.Abs(total-1)>1e-6f)throw new ArgumentException("Look sample weights are not normalized.");
            for(var i=0;i<samples.Length;i++)
            {
                var sample=samples[i];SampleSequence(sample.Sample,normalizedTime,_sample);
                for(var bone=0;bone<output.Length;bone++)output[bone]=i==0 ? AlsPrecisePoseBlender.Scale(_sample[bone],sample.Weight)
                    : AlsPrecisePoseBlender.Accumulate(output[bone],_sample[bone],sample.Weight);
            }
            if(samples.Length>1)for(var bone=0;bone<output.Length;bone++)output[bone]=output[bone].Normalized();
        }
    }
}

public static class AlsRefactoredLookPoseCompiler
{
    public static AlsRefactoredLookPoseSource Compile(string inputsJson,string graphsJson)
    {
        _=AlsRefactoredHeadSettingsCompiler.Compile(inputsJson,graphsJson);
        const string prefix="/ALS/ALS/Animations/";
        const string basis=prefix+"Base/A_Als_Stand_Pose.A_Als_Stand_Pose";
        string[] paths=[prefix+"View/A_Als_Look_Forward.A_Als_Look_Forward",prefix+"View/A_Als_Look_Down.A_Als_Look_Down",
            prefix+"View/A_Als_Look_Up.A_Als_Look_Up"];
        using var document=JsonDocument.Parse(inputsJson);var root=document.RootElement;
        var space=root.GetProperty("blendSpace");
        Require(space.GetProperty("source").GetString()==prefix+"View/BS_Als_Look.BS_Als_Look","Foreign Look BlendSpace.");
        var samples=space.GetProperty("samples").EnumerateArray().ToArray();Require(samples.Length==3,"Look sample closure differs.");
        float[] positions=[0,-90,90];
        for(var i=0;i<3;i++)
            Require(samples[i].GetProperty("sequence").GetString()==paths[i]&&samples[i].GetProperty("point").EnumerateArray()
                .Select(v=>v.GetSingle()).SequenceEqual(new[]{positions[i],0,0}),"Look sample binding differs.");
        foreach(var sequence in root.GetProperty("sequences").EnumerateArray())
        {
            var policy=sequence.GetProperty("evaluation");var path=sequence.GetProperty("raw").GetProperty("source").GetString();
            Require(policy.GetProperty("floatCurveCount").GetInt32()==0&&!policy.GetProperty("enableRootMotion").GetBoolean()&&
                !policy.GetProperty("forceRootLock").GetBoolean(),"Look currently requires empty curves and no root locking.");
            if(path==basis)Require(policy.GetProperty("additiveType").GetString()=="AAT_None","Additive Look base.");
            else Require(paths.Contains(path)&&policy.GetProperty("sequencePlayLength").GetSingle()==1&&
                policy.GetProperty("additiveType").GetString()=="AAT_RotationOffsetMeshSpace"&&
                policy.GetProperty("basePoseType").GetString()=="ABPT_AnimFrame"&&
                policy.GetProperty("baseAsset").GetString()==basis&&policy.GetProperty("baseFrame").GetInt32()==0,"Unsupported Look additive policy.");
        }
        var raw=AlsMantlingPoseCompiler.CompileRawAdditiveTargets(inputsJson,[..paths,basis]);
        Require(raw[basis].SkeletonPath=="/ALS/ALS/Character/SK_Als.SK_Als","Foreign Look skeleton.");
        return new(paths.Select(p=>raw[p]).ToArray(),raw[basis]);
    }
    private static void Require(bool value,string message){if(!value)throw new ArgumentException(message);}
}
